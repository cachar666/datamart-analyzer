using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DatamartAnalyzer.Mcp.Http;

/// <summary>
/// El datamart activo y los filtros de un usuario.
///
/// En el conector stdio esto vive en campos de BackendBase y dura lo que dura el
/// proceso, que es exactamente lo que dura la sesion. Sobre HTTP no hay nada
/// equivalente: se midio que el scope de DI se recrea en CADA llamada, incluso con
/// Mcp-Session-Id estable. Un BackendSql Scoped nace vacio en cada tool call.
///
/// Por eso el estado se guarda aparte, indexado por el token de acceso — que es lo
/// unico estable que acompana a todas las llamadas de un mismo usuario y que
/// sobrevive al modo stateless del protocolo.
/// </summary>
public sealed class EstadoSesion
{
    private Dictionary<string, List<string>> _filtros = new(StringComparer.OrdinalIgnoreCase);

    public string? Datamart { get; set; }

    public DateTime UltimoUsoUtc { get; private set; } = DateTime.UtcNow;

    public IReadOnlyDictionary<string, List<string>> Filtros => _filtros;

    public void Tocar() => UltimoUsoUtc = DateTime.UtcNow;

    public void ReemplazarFiltros(Dictionary<string, List<string>>? filtros)
    {
        _filtros = filtros is null
            ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, List<string>>(filtros, StringComparer.OrdinalIgnoreCase);
        Tocar();
    }

    public void LimpiarFiltros()
    {
        _filtros.Clear();
        Tocar();
    }

    /// <summary>Copia defensiva: quien la reciba no debe poder mutar el estado guardado.</summary>
    public Dictionary<string, List<string>> CopiaDeFiltros() =>
        new(_filtros, StringComparer.OrdinalIgnoreCase);
}

public interface IEstadoDeSesiones
{
    EstadoSesion Obtener(string clave);
}

/// <summary>
/// Los estados vivos, indexados por token.
///
/// Caducan por INACTIVIDAD, y eso no es un detalle de limpieza de memoria: un filtro
/// de empresa puesto ayer y olvidado sigue recortando cifras hoy, sin ninguna senal
/// visible. Prefiero que el usuario tenga que volver a fijarlo a que lea un numero
/// filtrado creyendo que es el total.
/// </summary>
public sealed class EstadoDeSesiones(TimeSpan expiracionPorInactividad) : IEstadoDeSesiones
{
    private readonly ConcurrentDictionary<string, EstadoSesion> _sesiones = new(StringComparer.Ordinal);

    public EstadoSesion Obtener(string clave)
    {
        // La purga va antes de resolver: si el estado del propio llamador ya vencio,
        // tiene que recibir uno limpio y no el viejo.
        Purgar();

        var estado = _sesiones.GetOrAdd(clave, _ => new EstadoSesion());
        estado.Tocar();
        return estado;
    }

    private void Purgar()
    {
        var corte = DateTime.UtcNow - expiracionPorInactividad;

        foreach (var (clave, estado) in _sesiones)
        {
            if (estado.UltimoUsoUtc >= corte) continue;

            // Se relee bajo TryRemove porque entre el chequeo y el borrado otra
            // llamada pudo haberlo tocado.
            if (_sesiones.TryGetValue(clave, out var actual) && actual.UltimoUsoUtc < corte)
                _sesiones.TryRemove(new KeyValuePair<string, EstadoSesion>(clave, actual));
        }
    }
}

/// <summary>
/// De que llamada es este estado.
///
/// La clave sale del token de acceso, pero el token NUNCA se usa crudo como indice:
/// se guarda su SHA-256. Un volcado de memoria o un log de diagnostico que muestre
/// las claves del diccionario no puede devolver credenciales de nadie.
/// </summary>
public sealed class ClaveDeSesion(IHttpContextAccessor contexto)
{
    /// <summary>
    /// Antes de que exista OAuth (fase A3) no hay token y todas las llamadas caen en
    /// la misma clave. Es correcto mientras el host escuche solo en 127.0.0.1 y lo
    /// use una sola persona; deja de serlo en el momento en que se publique, y por
    /// eso A4 no puede exponer el host sin que A3 este cerrado.
    /// </summary>
    public const string SinToken = "sin-token:desarrollo";

    public string Resolver()
    {
        var encabezado = contexto.HttpContext?.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(encabezado)) return SinToken;

        const string prefijo = "Bearer ";
        var token = encabezado.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
            ? encabezado[prefijo.Length..].Trim()
            : encabezado.Trim();

        if (token.Length == 0) return SinToken;

        return "tok:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
