using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DatamartAnalyzer.Mcp.Http.Auth;

/// <summary>Un codigo de autorizacion pendiente de canje. Vida corta y un solo uso.</summary>
public sealed record CodigoPendiente(
    string CredencialProtegida,
    string ClienteId,
    string RedirectUri,
    string? DesafioPkce,
    string? MetodoPkce,
    DateTime ExpiraUtc);

/// <summary>Un token emitido. Guarda la credencial cifrada, nunca en claro.</summary>
public sealed record TokenEmitido(
    string CredencialProtegida,
    string ClienteId,
    DateTime ExpiraUtc);

public interface IAlmacenDeAutorizacion
{
    string EmitirCodigo(CodigoPendiente codigo);

    /// <summary>Canjea el codigo. Devuelve null si no existe, vencio o ya se uso.</summary>
    CodigoPendiente? CanjearCodigo(string codigo);

    (string AccessToken, string RefreshToken, int ExpiraEnSegundos) EmitirTokens(
        string credencialProtegida, string clienteId);

    /// <summary>La credencial cifrada detras de un access token, o null si no vale.</summary>
    string? ResolverAccessToken(string token);

    /// <summary>Rota el refresh token. Devuelve null si no vale.</summary>
    (string AccessToken, string RefreshToken, int ExpiraEnSegundos)? Refrescar(string refreshToken);
}

/// <summary>
/// Codigos y tokens en memoria. Solo para desarrollo local (appsettings.json sin
/// BaseDeHost:CadenaConexion): reiniciar el host invalida todos los tokens y cada
/// persona tiene que volver a cargar sus cuatro datos. Para producción se decidió
/// SQL Server sobre Redis — es lo que ya se opera y respalda — en
/// <see cref="AlmacenDeAutorizacionEnSql"/>, con el mismo contrato detras de la
/// interfaz. Tampoco se resuelve con un archivo local: la decision 4 del plan pide
/// que el host no tenga estado en disco, para poder moverlo a Azure sin reescribirlo.
///
/// Los tokens se guardan HASHEADOS. Un volcado de memoria o un log que muestre las
/// claves del diccionario no puede devolver un token utilizable, del mismo modo que
/// el indice de sesiones guarda el SHA del token y no el token.
/// </summary>
public sealed class AlmacenDeAutorizacionEnMemoria(TimeSpan vidaDelAccessToken) : IAlmacenDeAutorizacion
{
    private readonly ConcurrentDictionary<string, CodigoPendiente> _codigos = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TokenEmitido> _accessTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TokenEmitido> _refreshTokens = new(StringComparer.Ordinal);

    /// <summary>El codigo vive lo justo para ir del navegador al cliente y volver.</summary>
    private static readonly TimeSpan VidaDelCodigo = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan VidaDelRefresh = TimeSpan.FromDays(30);

    private static string NuevoSecreto() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] datos) =>
        Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Indice por hash: el secreto en claro no queda guardado en ningun lado.</summary>
    private static string Indice(string secreto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secreto)));

    public string EmitirCodigo(CodigoPendiente codigo)
    {
        Purgar();
        var secreto = NuevoSecreto();
        _codigos[Indice(secreto)] = codigo with { ExpiraUtc = DateTime.UtcNow + VidaDelCodigo };
        return secreto;
    }

    public CodigoPendiente? CanjearCodigo(string codigo)
    {
        // TryRemove y no TryGetValue: un codigo de autorizacion se usa UNA vez. Si
        // alguien lo intercepta y lo canjea, el canje legitimo tiene que fallar y
        // dejar rastro, no funcionar dos veces en silencio.
        if (!_codigos.TryRemove(Indice(codigo), out var pendiente)) return null;
        return pendiente.ExpiraUtc > DateTime.UtcNow ? pendiente : null;
    }

    public (string AccessToken, string RefreshToken, int ExpiraEnSegundos) EmitirTokens(
        string credencialProtegida, string clienteId)
    {
        Purgar();

        var access = NuevoSecreto();
        var refresh = NuevoSecreto();
        var ahora = DateTime.UtcNow;

        _accessTokens[Indice(access)] = new TokenEmitido(credencialProtegida, clienteId, ahora + vidaDelAccessToken);
        _refreshTokens[Indice(refresh)] = new TokenEmitido(credencialProtegida, clienteId, ahora + VidaDelRefresh);

        return (access, refresh, (int)vidaDelAccessToken.TotalSeconds);
    }

    public string? ResolverAccessToken(string token)
    {
        if (!_accessTokens.TryGetValue(Indice(token), out var emitido)) return null;
        if (emitido.ExpiraUtc <= DateTime.UtcNow) return null;
        return emitido.CredencialProtegida;
    }

    public (string AccessToken, string RefreshToken, int ExpiraEnSegundos)? Refrescar(string refreshToken)
    {
        // Rotacion: el refresh usado se invalida al canjearlo. Si un refresh robado y
        // el legitimo se usan los dos, el segundo falla y el problema se nota.
        if (!_refreshTokens.TryRemove(Indice(refreshToken), out var emitido)) return null;
        if (emitido.ExpiraUtc <= DateTime.UtcNow) return null;

        return EmitirTokens(emitido.CredencialProtegida, emitido.ClienteId);
    }

    private void Purgar()
    {
        var ahora = DateTime.UtcNow;

        foreach (var (k, v) in _codigos)
            if (v.ExpiraUtc <= ahora) _codigos.TryRemove(k, out _);

        foreach (var (k, v) in _accessTokens)
            if (v.ExpiraUtc <= ahora) _accessTokens.TryRemove(k, out _);

        foreach (var (k, v) in _refreshTokens)
            if (v.ExpiraUtc <= ahora) _refreshTokens.TryRemove(k, out _);
    }
}
