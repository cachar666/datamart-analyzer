using DatamartAnalyzer.Core.Models;
using DatamartAnalyzer.Core.Store;

namespace DatamartAnalyzer.Mcp.Backends;

/// <summary>
/// Los cuatro datos que se le entregan al usuario final, convertidos en conexion:
///
///   Servidor SQL = datamart.sincoerp.com,4263
///   Base Datos   = SincoComercial_NuevaDW
///   Usuario      = UsuSincoComercial_NuevaDW
///   Clave        = ********
///
/// Reusa DatamartConexion —el mismo tipo con el que la API arma la cadena desde
/// dbo.ADPConfig o appsettings— para que la forma de conectarse sea una sola,
/// venga de donde venga la credencial.
/// </summary>
public sealed class ConexionDatamartDirecta : IConexionDatamart
{
    private readonly DatamartConexion _perfil;
    private readonly string _cadena;

    public string Servidor => _perfil.Servidor;
    public string Base => _perfil.Base;
    public string? Usuario => _perfil.Usuario;

    /// <summary>
    /// Sin usuario se cae a autenticacion de Windows. Es deliberado: dentro de la
    /// red de SINCO muchos datamarts se alcanzan asi y pedir un login de SQL que no
    /// existe seria un requisito inventado.
    /// </summary>
    public bool UsaAutenticacionSql => _perfil.UsaAutenticacionSql;

    public ConexionDatamartDirecta(string servidor, string basedatos, string? usuario, string? clave,
                                   int timeoutSegundos = 30)
    {
        if (string.IsNullOrWhiteSpace(servidor))
            throw new InvalidOperationException("Falta el Servidor SQL del datamart.");
        if (string.IsNullOrWhiteSpace(basedatos))
            throw new InvalidOperationException("Falta la Base Datos del datamart.");

        _perfil = new DatamartConexion(
            Nombre: basedatos.Trim(),
            Servidor: servidor.Trim(),
            Base: basedatos.Trim(),
            Usuario: string.IsNullOrWhiteSpace(usuario) ? null : usuario.Trim(),
            Clave: clave,
            Origen: "conector");

        if (!_perfil.EsValida(out var error))
            throw new InvalidOperationException(error);

        _cadena = _perfil.ConstruirConnectionString(timeoutSegundos);
    }

    /// <summary>
    /// Siempre la misma cadena: este conector tiene un solo datamart. Si llega otro
    /// nombre, es un error de quien llama — BackendSql lo detiene antes con un
    /// mensaje que explica que hay que cambiar la configuracion del conector.
    /// </summary>
    public Task<string> ResolverAsync(string database) => Task.FromResult(_cadena);
}
