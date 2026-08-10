using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Services;

/// <summary>
/// Instala el almacén (esquema ADP_DTM_APP) en un datamart que todavía no lo tiene,
/// al primer uso y solo en modo ERP.
///
/// No usa DbUp a propósito: dbup-sqlserver exige Microsoft.Data.SqlClient >= 6.1.4 y
/// la API está en 5.2.2. En vez de arrastrar ese salto a todo el proyecto, este
/// aplicador ejecuta LOS MISMOS scripts —el .csproj los enlaza desde
/// DatamartAnalyzer.Migrations\Scripts, no los copia— y escribe el journal con el
/// formato exacto de DbUp.
///
/// Gracias a eso los dos caminos se respetan:
///   · si la API instaló primero, `almacen.ps1` ve el journal y no repite el script;
///   · si el runner instaló primero, la API ve las tablas y no hace nada.
///
/// El LogicalName del recurso embebido replica el nombre que usa DbUp
/// ("DatamartAnalyzer.Migrations.Scripts.0001-create-store.sql"), que es la identidad
/// del script en la tabla SchemaVersions.
/// </summary>
public interface IStoreMigrator
{
    /// <summary>
    /// Deja el almacén listo en ese datamart. Devuelve true si quedó instalado
    /// (ya lo estaba o se instaló ahora). Nunca lanza: ante un fallo devuelve false
    /// y deja el motivo en <paramref name="error"/>.
    /// </summary>
    Task<(bool Ok, string? Error)> AsegurarInstaladoAsync(string database);

    /// <summary>Olvida lo cacheado para esa base (o para todas si viene null).</summary>
    void Olvidar(string? database = null);
}

public class StoreMigrator : IStoreMigrator
{
    private const string Schema = StoreRepository.Schema;   // ADP_DTM_APP
    private const string TablaJournal = "SchemaVersions";

    private readonly IDatamartConnectionResolver _conexiones;
    private readonly ILogger<StoreMigrator> _logger;

    /// <summary>Bases ya verificadas en esta ejecución: evita ir a la BD en cada request.</summary>
    private readonly ConcurrentDictionary<string, bool> _verificadas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Una instalación a la vez por proceso; entre procesos manda el applock de SQL.</summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Separador de lotes: una línea que sea sólo GO.</summary>
    private static readonly Regex SeparadorLotes =
        new(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public StoreMigrator(IDatamartConnectionResolver conexiones, ILogger<StoreMigrator> logger)
    {
        _conexiones = conexiones;
        _logger = logger;
    }

    public void Olvidar(string? database = null)
    {
        if (database is null) _verificadas.Clear();
        else _verificadas.TryRemove(database, out _);
    }

    public async Task<(bool Ok, string? Error)> AsegurarInstaladoAsync(string database)
    {
        if (_verificadas.TryGetValue(database, out var ok) && ok)
            return (true, null);

        await _lock.WaitAsync();
        try
        {
            if (_verificadas.TryGetValue(database, out ok) && ok)
                return (true, null);

            var resultado = await InstalarAsync(database);
            if (resultado.Ok) _verificadas[database] = true;
            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error instalando el almacén en {db}", database);
            return (false, ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(bool Ok, string? Error)> InstalarAsync(string database)
    {
        await using var conn = new SqlConnection(await _conexiones.ResolverAsync(database));
        await conn.OpenAsync();

        // Candado de aplicación: si hay dos instancias de la API apuntando al mismo
        // datamart, sólo una ejecuta el DDL y la otra espera y encuentra todo hecho.
        var recurso = $"DatamartAnalyzer.Store.{database}";

        var candado = await conn.ExecuteScalarAsync<int>("""
            DECLARE @resultado int;
            EXEC @resultado = sp_getapplock
                 @Resource = @rec, @LockMode = 'Exclusive',
                 @LockOwner = 'Session', @LockTimeout = 30000;
            SELECT @resultado;
            """, new { rec = recurso });

        if (candado < 0)
            return (false, "No se pudo tomar el candado de instalación (otra instancia lo tiene).");

        try
        {
            var scripts = LeerScripts();
            if (scripts.Count == 0)
                return (false, "No hay scripts de migración embebidos en la API.");

            await AsegurarEsquemaAsync(conn);
            await AsegurarJournalAsync(conn);

            var aplicados = (await conn.QueryAsync<string>(
                $"SELECT ScriptName FROM [{Schema}].[{TablaJournal}]")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var pendientes = scripts.Where(s => !aplicados.Contains(s.Nombre)).ToList();
            if (pendientes.Count == 0)
            {
                _logger.LogDebug("Almacén ya al día en {db}", database);
                return (true, null);
            }

            foreach (var script in pendientes)
            {
                _logger.LogInformation("Aplicando {script} en {db}", script.Nombre, database);

                await using var tx = await conn.BeginTransactionAsync();
                try
                {
                    foreach (var lote in SepararLotes(script.Contenido))
                        await conn.ExecuteAsync(lote, transaction: tx);

                    await conn.ExecuteAsync(
                        $"INSERT INTO [{Schema}].[{TablaJournal}] (ScriptName, Applied) VALUES (@n, GETDATE())",
                        new { n = script.Nombre }, tx);

                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }

            _logger.LogInformation(
                "Almacén instalado en {db}: {n} script(s) aplicado(s).", database, pendientes.Count);
            return (true, null);
        }
        catch (SqlException ex) when (ex.Number is 262 or 229 or 230 or 2760)
        {
            _logger.LogWarning(ex, "Sin permisos para crear el almacén en {db}", database);
            return (false,
                $"Sin permisos para crear el esquema {Schema} en '{database}'. " +
                "Se necesita CREATE SCHEMA y CREATE TABLE sobre esa base.");
        }
        finally
        {
            // Si la conexion quedo rota, soltar el candado tambien falla — y esa
            // excepcion taparia el error real. Se registra y se sigue: al cerrar la
            // sesion SQL libera el applock igual.
            try
            {
                await conn.ExecuteAsync(
                    "EXEC sp_releaseapplock @Resource = @rec, @LockOwner = 'Session';",
                    new { rec = recurso });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo liberar el candado de {db}", database);
            }
        }
    }

    private static async Task AsegurarEsquemaAsync(SqlConnection conn) =>
        await conn.ExecuteAsync($"""
            IF SCHEMA_ID('{Schema}') IS NULL EXEC('CREATE SCHEMA [{Schema}]');
            """);

    /// <summary>
    /// Tabla de journal con la MISMA forma que crea DbUp, para que los dos caminos
    /// lean y escriban lo mismo.
    /// </summary>
    private static async Task AsegurarJournalAsync(SqlConnection conn) =>
        await conn.ExecuteAsync($"""
            IF OBJECT_ID('[{Schema}].[{TablaJournal}]', 'U') IS NULL
            CREATE TABLE [{Schema}].[{TablaJournal}]
            (
                [Id]         int identity(1,1) NOT NULL
                             CONSTRAINT [PK_{TablaJournal}_Id] PRIMARY KEY,
                [ScriptName] nvarchar(255)     NOT NULL,
                [Applied]    datetime          NOT NULL
            );
            """);

    private static IEnumerable<string> SepararLotes(string script) =>
        SeparadorLotes.Split(script)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

    private record Script(string Nombre, string Contenido);

    /// <summary>
    /// Scripts embebidos, ordenados por nombre. El .csproj los enlaza desde el proyecto
    /// de migraciones con LogicalName "DatamartAnalyzer.Migrations.Scripts.*", así que
    /// el nombre coincide exactamente con el que usa DbUp en el journal.
    /// </summary>
    private static List<Script> LeerScripts()
    {
        var asm = Assembly.GetExecutingAssembly();

        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("DatamartAnalyzer.Migrations.Scripts.", StringComparison.Ordinal)
                     && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = asm.GetManifestResourceStream(n)!;
                using var lector = new StreamReader(stream);
                var contenido = lector.ReadToEnd().Replace("$schema$", Schema);
                return new Script(n, contenido);
            })
            .ToList();
    }
}
