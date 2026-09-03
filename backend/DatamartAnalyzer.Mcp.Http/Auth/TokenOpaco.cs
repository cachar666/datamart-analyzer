using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DatamartAnalyzer.Mcp.Http.Auth;

public static class TokenOpacoDefaults
{
    public const string Esquema = "TokenOpaco";

    /// <summary>Claim con la identidad estable del llamador: el hash de su credencial.</summary>
    public const string ClaimIdentidad = "datamart:identidad";
}

public sealed class TokenOpacoOptions : AuthenticationSchemeOptions;

/// <summary>
/// Valida el bearer token que emitio nuestro propio /token.
///
/// Los tokens son OPACOS y no JWT a proposito: detras de cada uno hay una credencial
/// de SQL Server que guardamos cifrada, y un JWT obligaria a que esa credencial —o una
/// referencia a ella— viajara en cada request y quedara en logs, proxies e historial
/// del navegador. Un token opaco es una llave sin contenido: quien lo intercepta
/// obtiene acceso, pero no obtiene la credencial del datamart.
/// </summary>
public sealed class TokenOpacoHandler(
    IOptionsMonitor<TokenOpacoOptions> opciones,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAlmacenDeAutorizacion almacen,
    ProtectorDeCredenciales protector)
    : AuthenticationHandler<TokenOpacoOptions>(opciones, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var encabezado = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(encabezado)) return Task.FromResult(AuthenticateResult.NoResult());

        const string prefijo = "Bearer ";
        if (!encabezado.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = encabezado[prefijo.Length..].Trim();
        if (token.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());

        var protegida = almacen.ResolverAccessToken(token);
        if (protegida is null) return Task.FromResult(AuthenticateResult.Fail("Token invalido o vencido."));

        CredencialDatamart credencial;
        try
        {
            credencial = CredencialDatamart.DeSerializado(protector.Descifrar(protegida));
        }
        catch (Exception ex)
        {
            // Si esto pasa, la llave de cifrado cambio o el almacen se corrompio. No es
            // culpa de quien llama, pero tampoco se puede seguir.
            Logger.LogError(ex, "No se pudo descifrar la credencial de un token valido.");
            return Task.FromResult(AuthenticateResult.Fail("No se pudo resolver la credencial."));
        }

        // La credencial viaja por HttpContext.Items y no por un claim: un claim se
        // serializa, se loguea y termina en lugares que no controlamos.
        Context.Items[CredencialDeLaSolicitud.Clave] = credencial;

        var identidad = CredencialDeLaSolicitud.Identidad(credencial);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identidad),
                new Claim(TokenOpacoDefaults.ClaimIdentidad, identidad),
                // Lo unico legible por humanos, y sin la clave.
                new Claim(ClaimTypes.Name, credencial.Descripcion),
            ],
            TokenOpacoDefaults.Esquema));

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, TokenOpacoDefaults.Esquema)));
    }
}

/// <summary>La credencial autenticada de esta solicitud, si la hay.</summary>
public static class CredencialDeLaSolicitud
{
    public const string Clave = "datamart:credencial";

    public static CredencialDatamart? De(HttpContext? ctx) =>
        ctx?.Items.TryGetValue(Clave, out var v) == true ? v as CredencialDatamart : null;

    /// <summary>
    /// Identidad estable del llamador: el hash de su credencial.
    ///
    /// Se usa como clave del estado de sesion en vez del token porque el token ROTA:
    /// al refrescarlo cambia, y si el estado colgara de el, los filtros se perderian
    /// en cada refresco sin ninguna razon visible para el usuario.
    ///
    /// Consecuencia que hay que tener presente: dos personas con la MISMA credencial
    /// comparten estado, porque para este servidor son la misma identidad. Es la misma
    /// limitacion que ya tiene CreatedBy en el almacen.
    /// </summary>
    public static string Identidad(CredencialDatamart c)
    {
        var material = $"{c.Servidor}␟{c.Base}␟{c.Usuario}␟{c.Clave}";
        return "cred:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
