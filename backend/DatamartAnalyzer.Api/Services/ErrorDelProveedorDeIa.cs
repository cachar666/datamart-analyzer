using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DatamartAnalyzer.Api.Services;

/// <summary>
/// El proveedor de IA respondio con un status de error.
///
/// El mensaje se arma con el status y, si el cuerpo los trae, con error.type y
/// error.code. NUNCA con el cuerpo ni con error.message: un 401 puede traer un
/// pedazo de la clave (el de OpenAI dice "Incorrect API key provided: sk-...abcd"),
/// y este mensaje termina en el log, en la respuesta de /api/analyze y, con el
/// conector en modo "via API", en Claude Desktop: DatamartApi.LanzarSiFalla
/// reenvia el campo error tal cual. La clave no se loguea ni truncada.
///
/// Gemelo de Features/DatamartAnalyzer/Llm/ErroresDelAsistente.cs en
/// ADPRO.Core.API. Portado a mano, no copiado: alla tambien hay OpenAI y una
/// auditoria que guarda este mensaje.
/// </summary>
public sealed class ErrorDelProveedorDeIa : HttpRequestException
{
    public string Proveedor { get; }
    public string? Tipo { get; }
    public string? Codigo { get; }

    /// <summary>
    /// type y code son identificadores del proveedor (authentication_error,
    /// rate_limit_error, ...). Solo se aceptan con esa forma: minusculas, digitos y
    /// guion bajo, cortos. Una clave tiene mayusculas y guiones y es mucho mas larga,
    /// asi que aunque algun dia un proveedor la pusiera ahi, no pasaria.
    /// </summary>
    private static readonly Regex Identificador = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant);

    private ErrorDelProveedorDeIa(string proveedor, HttpStatusCode estado, string? tipo, string? codigo)
        : base(Armar(proveedor, estado, tipo, codigo), null, estado)
    {
        Proveedor = proveedor;
        Tipo = tipo;
        Codigo = codigo;
    }

    /// <summary>El cuerpo se lee aca y no sale de aca.</summary>
    public static ErrorDelProveedorDeIa Desde(string proveedor, HttpStatusCode estado, string? cuerpo)
    {
        var (tipo, codigo) = Leer(cuerpo);
        return new ErrorDelProveedorDeIa(proveedor, estado, tipo, codigo);
    }

    // Anthropic: {"type":"error","error":{"type":"authentication_error","message":"..."}}
    // OpenAI:    {"error":{"message":"...","type":"invalid_request_error","code":"invalid_api_key"}}
    // Un cuerpo que no es JSON (un 502 de un proxy, p. ej.) deja solo el status.
    private static (string? Tipo, string? Codigo) Leer(string? cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo)) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
                return (null, null);

            return (Campo(error, "type"), Campo(error, "code"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? Campo(JsonElement error, string nombre) =>
        error.TryGetProperty(nombre, out var v)
        && v.ValueKind == JsonValueKind.String
        && v.GetString() is { } texto
        && Identificador.IsMatch(texto)
            ? texto
            : null;

    private static string Armar(string proveedor, HttpStatusCode estado, string? tipo, string? codigo)
    {
        // 529 (overloaded de Anthropic) no esta en el enum: ahi va solo el numero.
        var status = Enum.IsDefined(estado) ? $"{(int)estado} {estado}" : $"{(int)estado}";

        var detalle = string.Join(", ", new[] { tipo, codigo }.OfType<string>().Distinct());
        return detalle.Length == 0
            ? $"{proveedor} respondio {status}"
            : $"{proveedor} respondio {status} ({detalle})";
    }
}
