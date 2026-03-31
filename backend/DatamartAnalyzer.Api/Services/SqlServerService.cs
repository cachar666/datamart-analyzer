using Dapper;
using DatamartAnalyzer.Api.Models;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Services;

public interface ISqlServerService
{
    Task<List<DatabaseInfo>> ObtenerBasesDatosAsync();
    Task<List<DatabaseInfo>> RefrescarBasesDatosAsync();
    Task<bool> ExisteBaseDatosAsync(string database);
    Task<QueryResponse> EjecutarQueryAsync(string database, string sql);
    Task<bool> ProbarConexionAsync();
    DateTime? UltimaCargaBases { get; }
}

public class SqlServerService : ISqlServerService
{
    private readonly string _baseConnectionString;
    private readonly int _timeoutSeconds;
    private readonly int _maxRows;
    private readonly ILogger<SqlServerService> _logger;

    private List<DatabaseInfo>? _cachedDatabases;
    private DateTime? _ultimaCarga;
    private readonly SemaphoreSlim _cargaLock = new(1, 1);

    public DateTime? UltimaCargaBases => _ultimaCarga;

    private static readonly HashSet<string> BasesDatosSistema = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb"
    };

    public SqlServerService(IConfiguration config, ILogger<SqlServerService> logger)
    {
        _baseConnectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _timeoutSeconds = config.GetValue("QuerySettings:TimeoutSeconds", 30);
        _maxRows = config.GetValue("QuerySettings:MaxRows", 5000);
        _logger = logger;
    }

    private string BuildConnectionString(string? database = null)
    {
        var builder = new SqlConnectionStringBuilder(_baseConnectionString);
        if (!string.IsNullOrEmpty(database))
            builder.InitialCatalog = database;
        builder.CommandTimeout = _timeoutSeconds;
        return builder.ConnectionString;
    }

    public async Task<bool> ProbarConexionAsync()
    {
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString());
            await conn.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error probando conexión a YOSEMITE");
            return false;
        }
    }

    public async Task<List<DatabaseInfo>> ObtenerBasesDatosAsync()
    {
        if (_cachedDatabases is not null)
            return _cachedDatabases;

        return await RefrescarBasesDatosAsync();
    }

    public async Task<List<DatabaseInfo>> RefrescarBasesDatosAsync()
    {
        await _cargaLock.WaitAsync();
        try
        {
            _logger.LogInformation("Cargando lista de bases de datos desde YOSEMITE...");
            var resultado = await CargarBasesDatosDesdeServidorAsync();
            _cachedDatabases = resultado;
            _ultimaCarga = DateTime.Now;
            _logger.LogInformation("Cache de BDs actualizado: {count} bases encontradas.", resultado.Count);
            return resultado;
        }
        finally
        {
            _cargaLock.Release();
        }
    }

    public async Task<bool> ExisteBaseDatosAsync(string database)
    {
        // Primero revisa el cache
        if (_cachedDatabases is not null)
            return _cachedDatabases.Any(d => d.Nombre.Equals(database, StringComparison.OrdinalIgnoreCase));

        // Si no hay cache, consulta directo
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString("master"));
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sys.databases WHERE name = @name AND state_desc = 'ONLINE'",
                new { name = database });
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<DatabaseInfo>> CargarBasesDatosDesdeServidorAsync()
    {
        await using var conn = new SqlConnection(BuildConnectionString("master"));
        await conn.OpenAsync();

        // Obtener todas las BDs online en una sola query
        const string sql = @"
            SELECT name AS Nombre
            FROM sys.databases
            WHERE state_desc = 'ONLINE'
              AND name NOT IN ('master','model','msdb','tempdb')
            ORDER BY name";

        var bases = (await conn.QueryAsync<string>(sql)).ToList();

        // Verificar cuáles son datamart en paralelo (máx 30 conexiones simultáneas)
        var semaforo = new SemaphoreSlim(30);
        var tareas = bases.Select(async nombre =>
        {
            await semaforo.WaitAsync();
            try
            {
                bool esDatamart = await VerificarEsDatamartAsync(nombre);
                return new DatabaseInfo(nombre, esDatamart);
            }
            finally
            {
                semaforo.Release();
            }
        });

        var resultados = await Task.WhenAll(tareas);
        return resultados.OrderBy(d => d.Nombre).ToList();
    }

    private async Task<bool> VerificarEsDatamartAsync(string database)
    {
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(database));
            await conn.OpenAsync();

            var count = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.VIEWS
                WHERE TABLE_SCHEMA = 'ADP_DTM_VCONF'
                  AND TABLE_NAME = 'DefinicionesCamposTabla'");

            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<QueryResponse> EjecutarQueryAsync(string database, string sql)
    {
        // Validación de seguridad: solo SELECT
        var sqlTrimmed = sql.Trim();
        if (!sqlTrimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !sqlTrimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return new QueryResponse(false, null, 0, "Solo se permiten consultas SELECT.", 0);
        }

        // Palabras clave peligrosas
        var keywords = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE", "EXEC", "EXECUTE", "SP_" };
        foreach (var kw in keywords)
        {
            if (sqlTrimmed.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return new QueryResponse(false, null, 0, $"Operación no permitida: {kw}", 0);
        }

        // Agregar TOP si no tiene LIMIT/TOP
        if (!sqlTrimmed.Contains("TOP ", StringComparison.OrdinalIgnoreCase)
            && !sqlTrimmed.Contains("FETCH NEXT", StringComparison.OrdinalIgnoreCase))
        {
            // Insertar TOP después de SELECT [DISTINCT], no entre SELECT y DISTINCT
            var selectIdx = sqlTrimmed.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase) + 6;
            var afterSelect = sqlTrimmed[selectIdx..].TrimStart();
            var insertAt = selectIdx + (sqlTrimmed[selectIdx..].Length - afterSelect.Length);
            if (afterSelect.StartsWith("DISTINCT", StringComparison.OrdinalIgnoreCase))
                insertAt += "DISTINCT".Length;
            sqlTrimmed = sqlTrimmed.Insert(insertAt, $" TOP {_maxRows}");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(database));
            await conn.OpenAsync();

            using var cmd = new SqlCommand(sqlTrimmed, conn)
            {
                CommandTimeout = _timeoutSeconds
            };

            using var reader = await cmd.ExecuteReaderAsync();

            var columnas = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.GetName(i))
                .ToList();

            var filas = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync())
            {
                var fila = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    fila[columnas[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                filas.Add(fila);
            }

            sw.Stop();
            _logger.LogInformation("Query ejecutada en {database}: {filas} filas en {ms}ms",
                database, filas.Count, sw.ElapsedMilliseconds);

            return new QueryResponse(true, filas, filas.Count, null, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error ejecutando query en {database}", database);
            return new QueryResponse(false, null, 0, ex.Message, sw.Elapsed.TotalMilliseconds);
        }
    }
}
