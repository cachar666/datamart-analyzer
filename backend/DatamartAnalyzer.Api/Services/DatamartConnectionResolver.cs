using DatamartAnalyzer.Api.Models;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Services;

/// <summary>
/// Registro y resolución de conexiones a datamarts. Único lugar donde se decide
/// CÓMO se llega a un datamart, sin importar quién pregunte: el frontend dentro
/// del ERP, el frontend standalone o el conector MCP de Claude.
///
/// Un datamart se declara SIEMPRE con los mismos cuatro campos (servidor, base,
/// usuario, clave). Cambia sólo de dónde salen:
///
///   1. dbo.ADPConfig del ERP  → cuando la app corre dentro de un ERP (gana).
///   2. Sección "Datamarts" de appsettings → perfiles declarados a mano.
///   3. Enumeración del servidor de DefaultConnection → comportamiento histórico
///      (YOSEMITE con autenticación de Windows), para no romper lo que ya andaba.
///
/// Se resuelve por NOMBRE de perfil o por nombre de base, así el conector MCP y
/// el frontend pueden referirse al datamart con la etiqueta legible.
/// </summary>
public interface IDatamartConnectionResolver
{
    /// <summary>true si el datamart lo dicta el ERP (no se enumera el servidor local).</summary>
    bool ModoErp { get; }

    /// <summary>Perfiles declarados (ERP + appsettings). Sin credenciales.</summary>
    Task<IReadOnlyList<DatamartConexion>> ListarDeclaradosAsync();

    /// <summary>Perfil que corresponde a ese nombre o base, o null.</summary>
    Task<DatamartConexion?> BuscarAsync(string nombreOBase);

    /// <summary>Cadena de conexión para trabajar sobre ese datamart.</summary>
    Task<string> ResolverAsync(string nombreOBase);

    /// <summary>¿Se puede abrir la conexión? No se apoya en el catálogo local.</summary>
    Task<bool> EsAlcanzableAsync(string nombreOBase);

    /// <summary>Servidor donde queda, para mensajes y diagnóstico.</summary>
    Task<string> ServidorDeAsync(string nombreOBase);

    /// <summary>Nombre real de la base (lo que va en el SQL), resuelto desde el perfil.</summary>
    Task<string> BaseDeAsync(string nombreOBase);
}

public class DatamartConnectionResolver : IDatamartConnectionResolver
{
    private readonly string _defaultConnection;
    private readonly int _timeoutSeconds;
    private readonly List<DatamartConexion> _declaradosEnConfig;
    private readonly IErpContextService _erp;
    private readonly ILogger<DatamartConnectionResolver> _logger;

    public bool ModoErp => _erp.ModoErp;

    public DatamartConnectionResolver(
        IConfiguration config, IErpContextService erp, ILogger<DatamartConnectionResolver> logger)
    {
        _defaultConnection = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' no configurado.");
        _timeoutSeconds = config.GetValue("QuerySettings:TimeoutSeconds", 30);
        _erp = erp;
        _logger = logger;

        _declaradosEnConfig = LeerDeConfig(config);

        foreach (var d in _declaradosEnConfig)
        {
            if (!d.EsValida(out var error))
                _logger.LogWarning("Datamart '{n}' mal declarado en appsettings: {e}", d.Nombre, error);
            else
                _logger.LogInformation("Datamart declarado: {n} → {srv}/{db} ({auth})",
                    d.Nombre, d.Servidor, d.Base, d.UsaAutenticacionSql ? "SQL auth" : "Windows auth");
        }
    }

    private static List<DatamartConexion> LeerDeConfig(IConfiguration config)
    {
        var opciones = config.GetSection("Datamarts").Get<List<DatamartOptions>>() ?? [];

        return opciones
            .Where(o => !string.IsNullOrWhiteSpace(o.Servidor) && !string.IsNullOrWhiteSpace(o.Base))
            .Select(o => new DatamartConexion(
                string.IsNullOrWhiteSpace(o.Nombre) ? o.Base : o.Nombre,
                o.Servidor.Trim(),
                o.Base.Trim(),
                string.IsNullOrWhiteSpace(o.Usuario) ? null : o.Usuario.Trim(),
                o.Clave,
                Origen: "appsettings"))
            .ToList();
    }

    public async Task<IReadOnlyList<DatamartConexion>> ListarDeclaradosAsync()
    {
        var lista = new List<DatamartConexion>();

        // El ERP manda: si declara un datamart, es EL datamart de esta instalación.
        var delErp = await _erp.ObtenerDatamartDeclaradoAsync();
        if (delErp is not null)
        {
            lista.Add(delErp);
            return lista;   // modo ERP: uno solo, y no se mezcla con los de config
        }

        lista.AddRange(_declaradosEnConfig);
        return lista;
    }

    public async Task<DatamartConexion?> BuscarAsync(string nombreOBase)
    {
        if (string.IsNullOrWhiteSpace(nombreOBase)) return null;

        var declarados = await ListarDeclaradosAsync();

        return declarados.FirstOrDefault(d =>
                   d.Nombre.Equals(nombreOBase, StringComparison.OrdinalIgnoreCase))
            ?? declarados.FirstOrDefault(d =>
                   d.Base.Equals(nombreOBase, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> ResolverAsync(string nombreOBase)
    {
        if (string.IsNullOrWhiteSpace(nombreOBase))
            throw new ArgumentException("Se requiere el datamart.", nameof(nombreOBase));

        var perfil = await BuscarAsync(nombreOBase);
        if (perfil is not null)
        {
            if (!perfil.EsValida(out var error))
                throw new InvalidOperationException(error);

            return perfil.ConstruirConnectionString(_timeoutSeconds);
        }

        // Sin perfil declarado: comportamiento histórico — una base del servidor
        // de DefaultConnection con la autenticación de esa cadena.
        return new SqlConnectionStringBuilder(_defaultConnection)
        {
            InitialCatalog = nombreOBase,
            CommandTimeout = _timeoutSeconds
        }.ConnectionString;
    }

    public async Task<bool> EsAlcanzableAsync(string nombreOBase)
    {
        try
        {
            await using var conn = new SqlConnection(await ResolverAsync(nombreOBase));
            await conn.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "El datamart '{db}' no es alcanzable", nombreOBase);
            return false;
        }
    }

    public async Task<string> ServidorDeAsync(string nombreOBase) =>
        new SqlConnectionStringBuilder(await ResolverAsync(nombreOBase)).DataSource;

    public async Task<string> BaseDeAsync(string nombreOBase) =>
        (await BuscarAsync(nombreOBase))?.Base ?? nombreOBase;
}
