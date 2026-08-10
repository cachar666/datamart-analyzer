using System.Reflection;
using System.Text.RegularExpressions;
using DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DatamartAnalyzer.Migrations;

/// <summary>
/// Instala el almacén de consultas y dashboards DENTRO de cada base de datamart,
/// bajo el esquema ADP_DTM_APP. No crea bases de datos: trabaja sobre las que ya existen.
///
/// Uso:
///   dotnet run --project backend/DatamartAnalyzer.Migrations -- --list
///   dotnet run --project backend/DatamartAnalyzer.Migrations -- --database ADP_DTM_NORTE --what-if
///   dotnet run --project backend/DatamartAnalyzer.Migrations -- --database ADP_DTM_NORTE
///   dotnet run --project backend/DatamartAnalyzer.Migrations -- --pending
///   dotnet run --project backend/DatamartAnalyzer.Migrations -- --all
///
/// Cadena de conexión (al servidor, sin catálogo fijo):
///   1. --connection "..."
///   2. variable de entorno DATAMART_CONNECTION
///   3. ConnectionStrings:DefaultConnection de appsettings.json
/// </summary>
public static class Program
{
    private const string Schema = "ADP_DTM_APP";

    private const string DefaultConnection =
        "Server=YOSEMITE;Integrated Security=True;TrustServerCertificate=True;";

    /// <summary>Un datamart es una base que expone la vista de metadatos del ERP.</summary>
    private const string EsDatamartSql = """
        SELECT COUNT(1) FROM INFORMATION_SCHEMA.VIEWS
        WHERE TABLE_SCHEMA = 'ADP_DTM_VCONF' AND TABLE_NAME = 'DefinicionesCamposTabla'
        """;

    public static int Main(string[] args)
    {
        var whatIf = Tiene(args, "--what-if");
        var listar = Tiene(args, "--list");
        var todas = Tiene(args, "--all");
        var soloPendientes = Tiene(args, "--pending");
        var unaBase = Valor(args, "--database");
        var baseConn = ResolverConexion(args);

        Console.WriteLine("Datamart Analyzer — almacén de consultas y dashboards");
        Console.WriteLine($"Servidor : {new SqlConnectionStringBuilder(baseConn).DataSource}");
        Console.WriteLine($"Esquema  : {Schema}");
        Console.WriteLine();

        try
        {
            var datamarts = DescubrirDatamarts(baseConn);

            if (datamarts.Count == 0)
            {
                Console.WriteLine("No se encontró ninguna base con la vista ADP_DTM_VCONF.DefinicionesCamposTabla.");
                return 1;
            }

            if (listar)
            {
                Console.WriteLine($"Datamarts detectados ({datamarts.Count}):");
                foreach (var db in datamarts)
                {
                    var estado = EstadoAlmacen(baseConn, db);
                    Console.WriteLine($"  {db,-40} {estado}");
                }
                return 0;
            }

            List<string> objetivo;
            if (!string.IsNullOrWhiteSpace(unaBase))
            {
                var encontrada = datamarts.FirstOrDefault(d =>
                    d.Equals(unaBase, StringComparison.OrdinalIgnoreCase));

                if (encontrada is null)
                {
                    Console.WriteLine($"'{unaBase}' no es un datamart de este servidor.");
                    Console.WriteLine("Usá --list para ver las bases disponibles.");
                    return 1;
                }
                objetivo = [encontrada];
            }
            else if (soloPendientes)
            {
                // Caso "se creó un datamart nuevo": instala únicamente donde falta el
                // almacén y deja intactas las bases que ya lo tienen.
                objetivo = datamarts.Where(db => TablasDelAlmacen(baseConn, db) < 4).ToList();

                if (objetivo.Count == 0)
                {
                    Console.WriteLine($"Todos los datamarts ({datamarts.Count}) ya tienen el almacén instalado.");
                    return 0;
                }

                Console.WriteLine($"Datamarts sin almacén ({objetivo.Count} de {datamarts.Count}):");
                foreach (var db in objetivo) Console.WriteLine($"  - {db}");
                Console.WriteLine();
            }
            else if (todas)
            {
                objetivo = datamarts;
            }
            else
            {
                Console.WriteLine("Indicá sobre qué base trabajar:");
                Console.WriteLine("  --database <NOMBRE>   una sola base");
                Console.WriteLine("  --pending             solo los datamarts que aún no tienen almacén");
                Console.WriteLine("  --all                 todos los datamarts");
                Console.WriteLine("  --list                ver qué hay disponible");
                Console.WriteLine();
                Console.WriteLine("Agregá --what-if a cualquiera para simular sin aplicar nada.");
                return 1;
            }

            var fallos = 0;
            foreach (var db in objetivo)
            {
                if (!Migrar(baseConn, db, whatIf)) fallos++;
                Console.WriteLine();
            }

            if (fallos > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{fallos} de {objetivo.Count} base(s) fallaron.");
                Console.ResetColor();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(whatIf
                ? $"Simulación completa sobre {objetivo.Count} base(s). No se aplicó nada."
                : $"Listo. {objetivo.Count} base(s) al día.");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error:");
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            return 1;
        }
    }

    // ── Migración de una base ────────────────────────────────────────────────

    private static bool Migrar(string baseConn, string database, bool whatIf)
    {
        var conn = ParaBase(baseConn, database);
        Console.WriteLine($"── {database} ──────────────────────────────────");

        try
        {
            if (!whatIf) AsegurarEsquema(conn, database);

            var upgrader = DeployChanges.To
                .SqlDatabase(conn, Schema)
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
                .WithVariable("schema", Schema)
                .JournalToSqlTable(Schema, "SchemaVersions")
                .WithTransactionPerScript()
                .LogToConsole()
                .Build();

            var pendientes = upgrader.GetScriptsToExecute();
            if (pendientes.Count == 0)
            {
                Console.WriteLine("  Sin pendientes — al día.");
                return true;
            }

            Console.WriteLine($"  Pendientes ({pendientes.Count}):");
            foreach (var s in pendientes) Console.WriteLine($"    - {s.Name}");

            if (whatIf)
            {
                Console.WriteLine("  (simulación: no se aplicó nada)");
                return true;
            }

            var resultado = upgrader.PerformUpgrade();
            if (!resultado.Successful)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  FALLÓ en '{resultado.ErrorScript?.Name}': {resultado.Error?.Message}");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine($"  {pendientes.Count} migración(es) aplicada(s).");
            return true;
        }
        catch (SqlException ex) when (ex.Number == 262 || ex.Number == 229 || ex.Number == 230)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Sin permisos suficientes en {database}: {ex.Message}");
            Console.WriteLine($"  Se necesita CREATE SCHEMA y CREATE TABLE sobre esta base.");
            Console.ResetColor();
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    /// <summary>
    /// Crea el esquema si falta. DbUp crea sus tablas dentro del esquema pero no el
    /// esquema en sí, así que este paso va antes.
    /// </summary>
    private static void AsegurarEsquema(string connectionString, string database)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        // El nombre del esquema es una constante del programa, nunca entrada del usuario.
        cmd.CommandText = $"""
            IF SCHEMA_ID('{Schema}') IS NULL
            BEGIN
                EXEC('CREATE SCHEMA [{Schema}]');
                SELECT 1;
            END
            ELSE SELECT 0;
            """;

        var creado = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        if (creado == 1)
            Console.WriteLine($"  Esquema [{Schema}] creado.");
    }

    // ── Descubrimiento ───────────────────────────────────────────────────────

    private static List<string> DescubrirDatamarts(string baseConn)
    {
        var candidatas = new List<string>();

        using (var conn = new SqlConnection(ParaBase(baseConn, "master")))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT name FROM sys.databases
                WHERE state_desc = 'ONLINE'
                  AND name NOT IN ('master','model','msdb','tempdb')
                ORDER BY name
                """;
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) candidatas.Add(rd.GetString(0));
        }

        var datamarts = new List<string>();
        foreach (var db in candidatas)
        {
            try
            {
                using var conn = new SqlConnection(ParaBase(baseConn, db));
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = EsDatamartSql;
                if (Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0)
                    datamarts.Add(db);
            }
            catch
            {
                // Sin permisos sobre esa base, o no es datamart. Se ignora.
            }
        }

        return datamarts;
    }

    /// <summary>
    /// Cuántas de las 4 tablas del almacén existen en esa base.
    /// -1 si la base no es accesible.
    /// </summary>
    private static int TablasDelAlmacen(string baseConn, string database)
    {
        try
        {
            using var conn = new SqlConnection(ParaBase(baseConn, database));
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = '{Schema}'
                  AND TABLE_NAME IN ('SavedQueries','Dashboards','DashboardPanels','FilterPresets')
                """;
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch
        {
            return -1;
        }
    }

    private static string EstadoAlmacen(string baseConn, string database) =>
        TablasDelAlmacen(baseConn, database) switch
        {
            -1 => "no accesible",
            0 => "sin almacén  ← pendiente",
            4 => "almacén instalado",
            var n => $"almacén incompleto ({n}/4 tablas)  ← pendiente"
        };

    // ── Utilidades ───────────────────────────────────────────────────────────

    private static string ParaBase(string baseConn, string database) =>
        new SqlConnectionStringBuilder(baseConn) { InitialCatalog = database }.ConnectionString;

    private static bool Tiene(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static string? Valor(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string ResolverConexion(string[] args)
    {
        var explicita = Valor(args, "--connection");
        if (!string.IsNullOrWhiteSpace(explicita)) return explicita;

        var entorno = Environment.GetEnvironmentVariable("DATAMART_CONNECTION");
        if (!string.IsNullOrWhiteSpace(entorno)) return entorno;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetConnectionString("DefaultConnection") ?? DefaultConnection;
    }
}
