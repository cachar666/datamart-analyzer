using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace DatamartAnalyzer.Mcp.Http.Auth;

/// <summary>
/// El servidor de autorizacion. Es OAuth 2.1 pero ANGOSTO: no hay base de usuarios,
/// ni recuperacion de clave, ni segundo factor. La identidad es la credencial del
/// datamart, y la pantalla de login pide los mismos cuatro datos que hoy se cargan al
/// instalar el .mcpb.
///
/// Esto es lo que permite atender clientes fuera de SINCO el primer dia, sin depender
/// de que exista un directorio corporativo.
/// </summary>
public static class EndpointsDeAutorizacion
{
    public static void MapAutorizacion(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-authorization-server", MetadataDelEmisor);
        app.MapPost("/register", RegistrarCliente);
        app.MapGet("/authorize", MostrarFormulario);
        app.MapPost("/authorize", ProcesarFormulario);
        app.MapPost("/token", EmitirToken);
    }

    private static string BaseUrl(HttpRequest r) => $"{r.Scheme}://{r.Host}";

    // ── Metadata ─────────────────────────────────────────────────────────────

    private static IResult MetadataDelEmisor(HttpRequest req)
    {
        var b = BaseUrl(req);
        return Results.Json(new
        {
            issuer = b,
            authorization_endpoint = $"{b}/authorize",
            token_endpoint = $"{b}/token",
            registration_endpoint = $"{b}/register",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            // PKCE es obligatorio en OAuth 2.1, y S256 es el unico metodo que se
            // acepta: "plain" no protege de nada si el codigo se intercepta.
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none" },
        });
    }

    /// <summary>
    /// Registro dinamico de cliente.
    ///
    /// No hay secretos de cliente: el conector es un cliente PUBLICO —corre en
    /// infraestructura que no controlamos— y un secreto embebido ahi no es un secreto.
    /// Lo que protege el canje es PKCE, no el client_id. Por eso se acepta cualquier
    /// registro y se devuelve un identificador: sirve para trazar, no para autenticar.
    /// </summary>
    private static IResult RegistrarCliente(SolicitudDeRegistro? solicitud)
    {
        var id = "cli_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        return Results.Json(new
        {
            client_id = id,
            client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            token_endpoint_auth_method = "none",
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            redirect_uris = solicitud?.RedirectUris ?? [],
            client_name = solicitud?.ClientName,
        }, statusCode: 201);
    }

    // ── /authorize ───────────────────────────────────────────────────────────

    private static IResult MostrarFormulario(HttpRequest req)
    {
        var p = ParametrosDeAutorizacion.Leer(req.Query);
        if (p.Error is not null) return Results.BadRequest(new { error = "invalid_request", error_description = p.Error });

        return Html(Formulario(p, error: null));
    }

    private static async Task<IResult> ProcesarFormulario(
        HttpRequest req,
        ValidadorDeCredencial validador,
        ProtectorDeCredenciales protector,
        IAlmacenDeAutorizacion almacen,
        CancellationToken ct)
    {
        var form = await req.ReadFormAsync(ct);
        var p = ParametrosDeAutorizacion.Leer(form);
        if (p.Error is not null) return Results.BadRequest(new { error = "invalid_request", error_description = p.Error });

        var credencial = new CredencialDatamart(
            Servidor: (form["servidor"].ToString() ?? "").Trim(),
            Base: (form["base"].ToString() ?? "").Trim(),
            // Usuario vacio = autenticacion de Windows, igual que en el .mcpb.
            Usuario: Vacio(form["usuario"]) ? null : form["usuario"].ToString().Trim(),
            // La clave NO se recorta: un espacio al principio o al final puede ser parte.
            Clave: Vacio(form["clave"]) ? null : form["clave"].ToString());

        var (ok, error) = await validador.ValidarAsync(credencial, ct);
        if (!ok) return Html(Formulario(p, error), HttpStatusCode.OK);

        var codigo = almacen.EmitirCodigo(new CodigoPendiente(
            CredencialProtegida: protector.Cifrar(credencial.ASerializado()),
            ClienteId: p.ClientId!,
            RedirectUri: p.RedirectUri!,
            DesafioPkce: p.CodeChallenge,
            MetodoPkce: p.CodeChallengeMethod,
            ExpiraUtc: default));   // lo fija el almacen

        var sep = p.RedirectUri!.Contains('?') ? '&' : '?';
        var destino = $"{p.RedirectUri}{sep}code={Uri.EscapeDataString(codigo)}";
        if (!string.IsNullOrEmpty(p.State)) destino += $"&state={Uri.EscapeDataString(p.State)}";

        return Results.Redirect(destino);
    }

    private static bool Vacio(string? v) => string.IsNullOrWhiteSpace(v);

    // ── /token ───────────────────────────────────────────────────────────────

    private static async Task<IResult> EmitirToken(HttpRequest req, IAlmacenDeAutorizacion almacen)
    {
        var form = await req.ReadFormAsync();
        var grant = form["grant_type"].ToString();

        if (grant == "refresh_token")
        {
            var refrescado = almacen.Refrescar(form["refresh_token"].ToString() ?? "");
            if (refrescado is null) return ErrorOAuth("invalid_grant", "El refresh token no es valido o vencio.");

            return Results.Json(Respuesta(refrescado.Value));
        }

        if (grant != "authorization_code")
            return ErrorOAuth("unsupported_grant_type", $"grant_type '{grant}' no soportado.");

        var pendiente = almacen.CanjearCodigo(form["code"].ToString() ?? "");
        if (pendiente is null) return ErrorOAuth("invalid_grant", "El codigo no es valido, ya se uso o vencio.");

        // El redirect_uri del canje tiene que ser el mismo del /authorize. Sin este
        // chequeo, un codigo interceptado se puede canjear apuntando a otro lado.
        if (!string.Equals(pendiente.RedirectUri, form["redirect_uri"].ToString(), StringComparison.Ordinal))
            return ErrorOAuth("invalid_grant", "redirect_uri no coincide con el de la autorizacion.");

        if (!VerificarPkce(pendiente, form["code_verifier"].ToString(), out var motivo))
            return ErrorOAuth("invalid_grant", motivo!);

        var tokens = almacen.EmitirTokens(pendiente.CredencialProtegida, pendiente.ClienteId);
        return Results.Json(Respuesta(tokens));
    }

    private static object Respuesta((string AccessToken, string RefreshToken, int ExpiraEnSegundos) t) => new
    {
        access_token = t.AccessToken,
        token_type = "Bearer",
        expires_in = t.ExpiraEnSegundos,
        refresh_token = t.RefreshToken,
    };

    private static bool VerificarPkce(CodigoPendiente pendiente, string? verifier, out string? motivo)
    {
        motivo = null;

        // PKCE es obligatorio: sin desafio no se emite codigo, asi que llegar aca sin
        // el es un cliente que no lo mando en /authorize.
        if (string.IsNullOrEmpty(pendiente.DesafioPkce))
        {
            motivo = "La autorizacion se pidio sin PKCE, que es obligatorio.";
            return false;
        }

        if (string.IsNullOrEmpty(verifier))
        {
            motivo = "Falta code_verifier.";
            return false;
        }

        if (!string.Equals(pendiente.MetodoPkce, "S256", StringComparison.OrdinalIgnoreCase))
        {
            motivo = "Solo se acepta code_challenge_method=S256.";
            return false;
        }

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var calculado = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Comparacion de tiempo constante: comparar con == filtra, por el tiempo de
        // respuesta, cuantos caracteres coinciden.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(calculado),
                Encoding.ASCII.GetBytes(pendiente.DesafioPkce)))
        {
            motivo = "code_verifier no corresponde al desafio.";
            return false;
        }

        return true;
    }

    private static IResult ErrorOAuth(string error, string descripcion) =>
        Results.Json(new { error, error_description = descripcion }, statusCode: 400);

    // ── La pantalla ──────────────────────────────────────────────────────────

    private static IResult Html(string html, HttpStatusCode codigo = HttpStatusCode.OK) =>
        Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, (int)codigo);

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Formulario(ParametrosDeAutorizacion p, string? error) => $$"""
        <!doctype html>
        <html lang="es">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Conectar el datamart — ADPRO</title>
          <style>
            :root { color-scheme: light dark; }
            * { box-sizing: border-box; }
            body { margin:0; min-height:100vh; display:grid; place-items:center; padding:24px;
                   background:#0f172a; color:#e2e8f0;
                   font:15px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif; }
            .caja { width:100%; max-width:440px; background:#1e293b; border:1px solid #334155;
                    border-radius:12px; padding:28px; }
            h1 { margin:0 0 4px; font-size:19px; }
            p.sub { margin:0 0 22px; color:#94a3b8; font-size:13px; }
            label { display:block; margin:14px 0 5px; font-size:13px; font-weight:600; }
            .pista { font-weight:400; color:#94a3b8; }
            input { width:100%; padding:9px 11px; border-radius:7px; border:1px solid #475569;
                    background:#0f172a; color:#e2e8f0; font-size:14px; }
            input:focus { outline:2px solid #3b82f6; outline-offset:1px; border-color:transparent; }
            button { width:100%; margin-top:22px; padding:11px; border:0; border-radius:7px;
                     background:#2563eb; color:#fff; font-size:15px; font-weight:600; cursor:pointer; }
            button:hover { background:#1d4ed8; }
            .error { margin:0 0 16px; padding:11px 13px; border-radius:7px;
                     background:#7f1d1d; border:1px solid #b91c1c; color:#fecaca; font-size:13px; }
            .nota { margin:18px 0 0; color:#94a3b8; font-size:12px; }
          </style>
        </head>
        <body>
          <form class="caja" method="post" action="/authorize">
            <h1>Conectar el datamart</h1>
            <p class="sub">Los mismos cuatro datos que te entregaron para el conector.</p>

            {{(error is null ? "" : $"<div class=\"error\">{E(error)}</div>")}}

            <input type="hidden" name="client_id" value="{{E(p.ClientId)}}">
            <input type="hidden" name="redirect_uri" value="{{E(p.RedirectUri)}}">
            <input type="hidden" name="state" value="{{E(p.State)}}">
            <input type="hidden" name="code_challenge" value="{{E(p.CodeChallenge)}}">
            <input type="hidden" name="code_challenge_method" value="{{E(p.CodeChallengeMethod)}}">
            <input type="hidden" name="response_type" value="code">

            <label>Servidor SQL
              <span class="pista">— con el puerto si lo tiene</span></label>
            <input name="servidor" required placeholder="datamart.sincoerp.com,4263" autofocus>

            <label>Base de datos</label>
            <input name="base" required placeholder="SincoComercial_NuevaDW">

            <label>Usuario <span class="pista">— vacío usa tu cuenta de Windows</span></label>
            <input name="usuario" autocomplete="off" placeholder="UsuSincoComercial_NuevaDW">

            <label>Clave <span class="pista">— vacía si dejaste el usuario vacío</span></label>
            <input name="clave" type="password" autocomplete="off">

            <button type="submit">Conectar</button>

            <p class="nota">Se comprueba la conexión antes de continuar. Solo lectura:
            el conector nunca modifica ni borra información del datamart.</p>
          </form>
        </body>
        </html>
        """;
}

public sealed record SolicitudDeRegistro(
    [property: JsonPropertyName("redirect_uris")] string[]? RedirectUris,
    [property: JsonPropertyName("client_name")] string? ClientName);

/// <summary>Los parametros de OAuth que viajan por query en /authorize y por form en el POST.</summary>
public sealed record ParametrosDeAutorizacion(
    string? ClientId,
    string? RedirectUri,
    string? State,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? Error)
{
    public static ParametrosDeAutorizacion Leer(IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> origen)
    {
        var d = origen.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        string? V(string k) => d.TryGetValue(k, out var x) && !string.IsNullOrWhiteSpace(x) ? x : null;

        var clientId = V("client_id");
        var redirect = V("redirect_uri");
        var desafio = V("code_challenge");
        var metodo = V("code_challenge_method") ?? "S256";

        string? error = null;
        if (V("response_type") is not "code") error = "response_type tiene que ser 'code'.";
        else if (clientId is null) error = "Falta client_id.";
        else if (redirect is null) error = "Falta redirect_uri.";
        // Se exige PKCE ACA y no en /token: rechazarlo al final significaria haber
        // mostrado el formulario y pedido la credencial para nada.
        else if (desafio is null) error = "Falta code_challenge: PKCE es obligatorio.";
        else if (!string.Equals(metodo, "S256", StringComparison.OrdinalIgnoreCase))
            error = "Solo se acepta code_challenge_method=S256.";

        return new ParametrosDeAutorizacion(clientId, redirect, V("state"), desafio, metodo, error);
    }
}
