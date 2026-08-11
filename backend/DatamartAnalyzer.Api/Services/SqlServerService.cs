using Dapper;
using DatamartAnalyzer.Core.Models;
using DatamartAnalyzer.Core.Sql;
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
    private readonly IDatamartConnectionResolver _conexiones;
    private readonly ISqlExecutor _executor;
    private readonly ILogger<SqlServerService> _logger;

    private List<DatabaseInfo>? _cachedDatabases;
    private DateTime? _ultimaCarga;
    private readonly SemaphoreSlim _cargaLock = new(1, 1);

    public DateTime? UltimaCargaBases => _ultimaCarga;

    private static readonly HashSet<string> BasesDatosSistema = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb"
    };

    public SqlServerService(IConfiguration config, IDatamartConnectionResolver conexiones,
                            ISqlExecutor executor, ILogger<SqlServerService> logger)
    {
        _conexiones = conexiones;
        _executor = executor;
        _baseConnectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _timeoutSeconds = config.GetValue("QuerySettings:TimeoutSeconds", 30);
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
        // Si hay un perfil declarado (ERP o appsettings) la pregunta no es "existe en
        // este servidor" sino "se puede abrir": la base puede estar en otro lado.
        if (await _conexiones.BuscarAsync(database) is not null)
            return await _conexiones.EsAlcanzableAsync(database);

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
        var declarados = await _conexiones.ListarDeclaradosAsync();

        // En modo ERP el datamart lo dicta dbo.ADPConfig: no se enumera nada mas,
        // aunque el servidor local tenga otras bases.
        if (_conexiones.ModoErp)
        {
            var soloErp = new List<DatabaseInfo>();
            foreach (var d in declarados)
                soloErp.Add(new DatabaseInfo(d.Base, await _conexiones.EsAlcanzableAsync(d.Base)));

            _logger.LogInformation("Modo ERP: {n} datamart(s) declarado(s).", soloErp.Count);
            return soloErp;
        }

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

        var resultados = (await Task.WhenAll(tareas)).ToList();

        // Los datamarts declarados en appsettings pueden estar en OTRO servidor y por
        // eso no aparecen en la enumeracion local. Se suman sin duplicar.
        foreach (var d in declarados)
        {
            if (resultados.Any(r => r.Nombre.Equals(d.Base, StringComparison.OrdinalIgnoreCase)))
                continue;

            resultados.Add(new DatabaseInfo(d.Base, await _conexiones.EsAlcanzableAsync(d.Base)));
        }

        return resultados.OrderBy(d => d.Nombre).ToList();
    }

    private async Task<bool> VerificarEsDatamartAsync(string database)
    {
        try
        {
            await using var conn = new SqlConnection(await _conexiones.ResolverAsync(database));
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

    /// <summary>
    /// Delega en Core.Sql.SqlExecutor. Los filtros ya vienen inyectados por quien
    /// llama —el controlador necesita el SQL final como texto para devolvérselo al
    /// usuario— asi que aca no se vuelven a inyectar.
    /// </summary>
    public Task<QueryResponse> EjecutarQueryAsync(string database, string sql) =>
        _executor.EjecutarAsync(database, sql);
}
