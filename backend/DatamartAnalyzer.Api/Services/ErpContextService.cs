using System.Text.RegularExpressions;
using Dapper;
using DatamartAnalyzer.Api.Models;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Services;

/// <summary>
/// Lee de dbo.ADPConfig del ERP qué datamart le corresponde a esta instalación.
///
/// Se declara con los MISMOS cuatro campos que en cualquier otro lado — cuatro
/// claves, todas dentro del varchar(20) de CnfCodigo:
///
///   DATAMARTSRV   servidor  (ej. datamart.sincoerp.com,4263) · vacío = el del ERP
///   DATAMART      base      (ej. SincoEthussTCoindustrialDW)  · obligatoria
///   DATAMARTUSR   usuario   · vacío = autenticación de Windows
///   DATAMARTPWD   clave
///
/// Sin Erp:ConnectionString la app corre en modo STANDALONE y usa la sección
/// "Datamarts" de appsettings.
///
/// Solo lectura: nunca escribe en la tabla de configuración del ERP.
/// SEGURIDAD: la clave sale por ObtenerDatamartDeclaradoAsync (uso interno) y
/// NUNCA viaja en ErpContextDto.
/// </summary>
public interface IErpContextService
{
    bool ModoErp { get; }
    Task<ErpContextDto> ResolverAsync();

    /// <summary>Perfil completo con credenciales, o null. NO exponer al cliente.</summary>
    Task<DatamartConexion?> ObtenerDatamartDeclaradoAsync();

    void InvalidarCache();
}

public class ErpContextService : IErpContextService
{
    private readonly string? _erpConnection;
    private readonly string _tabla;
    private readonly string _colClave;
    private readonly string _colValor;
    private readonly string _kBase, _kServidor, _kUsuario, _kClave;
    private readonly ILogger<ErpContextService> _logger;

    private ErpContextDto? _cache;
    private DatamartConexion? _perfil;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly Regex IdentificadorValido = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// dbo.ADPConfig.CnfCodigo es varchar(20). Una clave más larga nunca haría match y el
    /// síntoma sería "el ERP no declara datamart" — un error mudo. Se valida al arrancar.
    /// </summary>
    private const int MaxLargoClave = 20;

    public bool ModoErp => !string.IsNullOrWhiteSpace(_erpConnection);

    public ErpContextService(IConfiguration config, ILogger<ErpContextService> logger)
    {
        _logger = logger;

        _erpConnection = config.GetConnectionString("ErpConnection");
        _tabla = config["Erp:ConfigTable"] ?? "dbo.ADPConfig";
        _colClave = config["Erp:KeyColumn"] ?? "CnfCodigo";
        _colValor = config["Erp:ValueColumn"] ?? "CnfValor";
        _kBase = config["Erp:DatamartKey"] ?? "DATAMART";
        _kServidor = config["Erp:ServerKey"] ?? "DATAMARTSRV";
        _kUsuario = config["Erp:UserKey"] ?? "DATAMARTUSR";
        _kClave = config["Erp:PasswordKey"] ?? "DATAMARTPWD";

        if (ModoErp)
        {
            ValidarConfiguracion();
            var erp = new SqlConnectionStringBuilder(_erpConnection!);
            _logger.LogInformation("Modo ERP activo. ERP: {srv}/{db} · claves en {tabla}",
                erp.DataSource, erp.InitialCatalog, _tabla);
        }
        else
        {
            _logger.LogInformation(
                "Sin Erp:ConnectionString — modo standalone: se usan los datamarts de appsettings.");
        }
    }

    private void ValidarConfiguracion()
    {
        var partes = PartesDeTabla();
        if (partes.Length is < 1 or > 2 || !partes.All(p => IdentificadorValido.IsMatch(p)))
            throw new InvalidOperationException(
                $"Erp:ConfigTable inválido: '{_tabla}'. Se espera 'esquema.tabla' o 'tabla'.");

        foreach (var (nombre, valor) in new[] { ("Erp:KeyColumn", _colClave), ("Erp:ValueColumn", _colValor) })
            if (!IdentificadorValido.IsMatch(valor))
                throw new InvalidOperationException($"{nombre} inválido: '{valor}'.");

        foreach (var (nombre, valor) in new[]
                 {
                     ("Erp:DatamartKey", _kBase), ("Erp:ServerKey", _kServidor),
                     ("Erp:UserKey", _kUsuario), ("Erp:PasswordKey", _kClave)
                 })
        {
            if (string.IsNullOrWhiteSpace(valor))
                throw new InvalidOperationException($"{nombre} no puede estar vacío.");

            if (valor.Length > MaxLargoClave)
                throw new InvalidOperationException(
                    $"{nombre} = '{valor}' tiene {valor.Length} caracteres y la columna {_colClave} " +
                    $"admite {MaxLargoClave}. Nunca encontraría el registro.");
        }
    }

    private string[] PartesDeTabla() =>
        _tabla.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim('[', ']')).ToArray();

    private string EsquemaDeTabla() { var p = PartesDeTabla(); return p.Length == 2 ? p[0] : "dbo"; }
    private string NombreDeTabla() => PartesDeTabla().Last();
    private string TablaEscapada() => $"[{EsquemaDeTabla()}].[{NombreDeTabla()}]";

    public void InvalidarCache()
    {
        _cache = null;
        _perfil = null;
    }

    public async Task<DatamartConexion?> ObtenerDatamartDeclaradoAsync()
    {
        await ResolverAsync();
        return _perfil;
    }

    public async Task<ErpContextDto> ResolverAsync()
    {
        if (!ModoErp)
            return new ErpContextDto(false, null, null, null, null, false,
                "Modo standalone: no hay ERP configurado, se usan los datamarts de appsettings.");

        if (_cache is not null) return _cache;

        await _lock.WaitAsync();
        try
        {
            if (_cache is not null) return _cache;
            _cache = await ResolverInternoAsync();
            return _cache;
        }
        finally { _lock.Release(); }
    }

    private async Task<ErpContextDto> ResolverInternoAsync()
    {
        var erp = new SqlConnectionStringBuilder(_erpConnection!);
        var erpServidor = erp.DataSource;
        var erpDb = erp.InitialCatalog;

        try
        {
            await using var conn = new SqlConnection(_erpConnection);
            await conn.OpenAsync();

            var existeTabla = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @esq AND TABLE_NAME = @tab",
                new { esq = EsquemaDeTabla(), tab = NombreDeTabla() });

            if (existeTabla == 0)
                return Fallo(erpServidor, erpDb, $"La base del ERP '{erpDb}' no tiene la tabla {_tabla}.");

            // Las cuatro claves en una sola ida. Tabla y columnas vienen de appsettings
            // y ya pasaron por ValidarConfiguracion; las claves van como parámetros.
            var filas = await conn.QueryAsync<(string Clave, string? Valor)>($"""
                SELECT CAST([{_colClave}] AS varchar(20))  AS Clave,
                       CAST([{_colValor}] AS varchar(800)) AS Valor
                FROM {TablaEscapada()}
                WHERE [{_colClave}] IN (@kBase, @kSrv, @kUsr, @kPwd)
                """,
                new { kBase = _kBase, kSrv = _kServidor, kUsr = _kUsuario, kPwd = _kClave });

            var mapa = filas
                .Where(f => !string.IsNullOrWhiteSpace(f.Valor))
                .ToDictionary(f => f.Clave.Trim(), f => f.Valor!.Trim(), StringComparer.OrdinalIgnoreCase);

            mapa.TryGetValue(_kBase, out var baseDatos);
            mapa.TryGetValue(_kServidor, out var servidor);
            mapa.TryGetValue(_kUsuario, out var usuario);
            mapa.TryGetValue(_kClave, out var clave);

            if (string.IsNullOrWhiteSpace(baseDatos))
                return Fallo(erpServidor, erpDb,
                    $"El ERP '{erpDb}' no declara ningún datamart. " +
                    $"Falta el registro con {_colClave} = '{_kBase}' en {_tabla}.");

            // Sin DATAMARTSRV se asume el mismo servidor del ERP.
            servidor = string.IsNullOrWhiteSpace(servidor) ? erpServidor : servidor;

            var perfil = new DatamartConexion(
                Nombre: baseDatos, Servidor: servidor, Base: baseDatos,
                Usuario: string.IsNullOrWhiteSpace(usuario) ? null : usuario,
                Clave: clave, Origen: $"ERP {erpDb}");

            if (!perfil.EsValida(out var errorPerfil))
                return Fallo(erpServidor, erpDb, $"El ERP declara un datamart incompleto: {errorPerfil}");

            return await VerificarAsync(erpServidor, erpDb, perfil);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error leyendo {tabla} en el ERP {srv}/{db}", _tabla, erpServidor, erpDb);
            return Fallo(erpServidor, erpDb,
                $"No se pudo leer la configuración del ERP '{erpDb}' en {erpServidor}: {ex.Message}");
        }
    }

    /// <summary>
    /// Abre la conexión al datamart y confirma que sea uno de verdad. No se apoya en el
    /// catálogo local: la base puede estar en otro servidor y con otras credenciales.
    /// </summary>
    private async Task<ErpContextDto> VerificarAsync(string erpServidor, string erpDb, DatamartConexion perfil)
    {
        try
        {
            await using var conn = new SqlConnection(perfil.ConstruirConnectionString());
            await conn.OpenAsync();

            var esDatamart = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.VIEWS
                WHERE TABLE_SCHEMA = 'ADP_DTM_VCONF' AND TABLE_NAME = 'DefinicionesCamposTabla'
                """);

            if (esDatamart == 0)
            {
                _logger.LogWarning(
                    "El ERP {db} declara {srv}/{dm}, pero no expone ADP_DTM_VCONF.DefinicionesCamposTabla",
                    erpDb, perfil.Servidor, perfil.Base);

                _perfil = null;
                return new ErpContextDto(true, erpServidor, erpDb, perfil.Servidor, perfil.Base, false,
                    $"La base '{perfil.Base}' en {perfil.Servidor} existe pero no parece un datamart: " +
                    "no expone ADP_DTM_VCONF.DefinicionesCamposTabla.");
            }

            _perfil = perfil;
            _logger.LogInformation("ERP {erpSrv}/{erpDb} → datamart {srv}/{dm} ({auth})",
                erpServidor, erpDb, perfil.Servidor, perfil.Base,
                perfil.UsaAutenticacionSql ? "SQL auth" : "Windows auth");

            return new ErpContextDto(true, erpServidor, erpDb, perfil.Servidor, perfil.Base, true, null);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "No se pudo conectar al datamart {srv}/{dm}", perfil.Servidor, perfil.Base);
            _perfil = null;
            return new ErpContextDto(true, erpServidor, erpDb, perfil.Servidor, perfil.Base, false,
                $"El ERP declara '{perfil.Base}' en {perfil.Servidor}, pero no se pudo conectar: {ex.Message}");
        }
    }

    private ErpContextDto Fallo(string erpServidor, string erpDb, string mensaje)
    {
        _perfil = null;
        _logger.LogWarning("Contexto ERP no resuelto: {mensaje}", mensaje);
        return new ErpContextDto(true, erpServidor, erpDb, null, null, false, mensaje);
    }
}
