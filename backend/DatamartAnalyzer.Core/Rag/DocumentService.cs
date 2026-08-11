using System.Text;
using System.Text.RegularExpressions;

namespace DatamartAnalyzer.Core.Rag;

public interface IDocumentService
{
    string ObtenerContextoErp();
    string ObtenerSeccionesRelevantes(string pregunta, int maxChars = 3000);
    bool HayDocumento { get; }
}

/// <summary>
/// De donde sale el corpus del ERP. La API puede apuntar a un archivo del repo
/// (asi editar docs\contexto-erp.md se ve sin recompilar); el conector MCP corre
/// en la maquina del usuario final, donde ese archivo no existe, y usa el recurso
/// embebido. Si no se declara ruta, o la ruta no existe, cae al embebido.
/// </summary>
public sealed class RagOptions
{
    public string? DocumentPath { get; set; }
}

public class DocumentService : IDocumentService
{
    /// <summary>Nombre logico del corpus embebido (ver DatamartAnalyzer.Core.csproj).</summary>
    private const string RecursoEmbebido = "DatamartAnalyzer.Core.contexto-erp.md";

    private readonly string _textoCompleto;
    private readonly List<(string Titulo, string Contenido)> _secciones;
    private readonly ILogger<DocumentService> _logger;

    public bool HayDocumento => !string.IsNullOrWhiteSpace(_textoCompleto);

    public DocumentService(RagOptions? opciones, ILogger<DocumentService> logger)
    {
        _logger = logger;
        _textoCompleto = "";
        _secciones = [];

        var ruta = opciones?.DocumentPath;
        var origen = "recurso embebido";

        try
        {
            // Solo texto plano. Hubo soporte de .docx, pero el corpus es
            // docs\contexto-erp.md desde hace rato, y arrastrar DocumentFormat.OpenXml
            // costaba 6,5 MB en el paquete del conector por una funcion que ya nadie
            // llamaba.
            var textoRaw = LeerDeArchivo(ruta, ref origen);
            textoRaw ??= LeerEmbebido();

            if (string.IsNullOrWhiteSpace(textoRaw))
            {
                _logger.LogWarning("No se pudo cargar el contexto ERP (ni archivo ni recurso embebido).");
                return;
            }

            _textoCompleto = LimpiarTexto(textoRaw);
            _secciones = SplitEnSecciones(_textoCompleto);
            _logger.LogInformation(
                "Documento ERP cargado desde {origen}: {chars} chars, {secciones} secciones",
                origen, _textoCompleto.Length, _secciones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo el contexto ERP desde {origen}", origen);
            _textoCompleto = "";
            _secciones = [];
        }
    }

    /// <summary>
    /// El archivo declarado, si existe y es texto. Devuelve null para que el llamador
    /// caiga al recurso embebido — y deja dicho por que en el log, porque "no aparece
    /// mi cambio en contexto-erp.md" es la pregunta que se hace despues.
    /// </summary>
    private string? LeerDeArchivo(string? ruta, ref string origen)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;

        if (!File.Exists(ruta))
        {
            _logger.LogWarning("Contexto ERP no encontrado en {ruta}; se usa el embebido.", ruta);
            return null;
        }

        if (Path.GetExtension(ruta).ToLowerInvariant() is not (".md" or ".txt"))
        {
            _logger.LogWarning("El contexto ERP debe ser .md o .txt; {ruta} no lo es. Se usa el embebido.", ruta);
            return null;
        }

        origen = ruta;
        return File.ReadAllText(ruta);
    }

    private static string? LeerEmbebido()
    {
        var asm = typeof(DocumentService).Assembly;
        using var s = asm.GetManifestResourceStream(RecursoEmbebido);
        if (s is null) return null;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Retorna el documento completo (para bloque cacheado).</summary>
    public string ObtenerContextoErp() => _textoCompleto;

    /// <summary>
    /// Retorna solo las secciones del documento relevantes para la pregunta dada,
    /// hasta <paramref name="maxChars"/> caracteres. Siempre incluye la primera sección
    /// (intro general) y luego las más relevantes por palabras clave.
    /// </summary>
    public string ObtenerSeccionesRelevantes(string pregunta, int maxChars = 3000)
    {
        if (!HayDocumento) return "";
        if (_secciones.Count == 0) return _textoCompleto[..Math.Min(_textoCompleto.Length, maxChars)];

        var palabras = ExtraerPalabras(pregunta);

        // Puntúa cada sección por coincidencia de palabras clave
        var puntuadas = _secciones
            .Select((s, i) => (
                Indice: i,
                Seccion: s,
                Score: palabras.Sum(p =>
                    (s.Titulo.Contains(p, StringComparison.OrdinalIgnoreCase) ? 3 : 0) +
                    (s.Contenido.Contains(p, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            ))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Indice)   // desempate: secciones más tempranas primero
            .ToList();

        var sb = new StringBuilder();

        // Siempre incluir primera sección (intro/overview)
        var intro = _secciones[0];
        var introTexto = FormatSeccion(intro);
        if (introTexto.Length <= maxChars)
            sb.Append(introTexto);

        // Agregar secciones relevantes hasta el límite.
        // Nota: una sección que no quepa se SALTA (continue), no aborta el bucle —
        // las secciones siguientes son más pequeñas y suelen caber. Con break, una
        // sección grande de score alto bloqueaba toda la recuperación posterior.
        foreach (var (_, seccion, score) in puntuadas)
        {
            if (score == 0) break;
            if (seccion == intro) continue;   // ya incluida

            var texto = FormatSeccion(seccion);
            if (sb.Length + texto.Length > maxChars) continue;
            sb.Append(texto);
        }

        return sb.ToString().Trim();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string FormatSeccion((string Titulo, string Contenido) s)
        => string.IsNullOrEmpty(s.Titulo)
            ? s.Contenido + "\n"
            : $"## {s.Titulo}\n{s.Contenido}\n";

    /// <summary>Divide el texto en secciones por encabezados markdown (## o #).</summary>
    private static List<(string Titulo, string Contenido)> SplitEnSecciones(string texto)
    {
        var secciones = new List<(string, string)>();
        var titulo = "";
        var contenido = new StringBuilder();

        foreach (var linea in texto.Split('\n'))
        {
            var m = Regex.Match(linea, @"^#{1,3}\s+(.+)");
            if (m.Success)
            {
                var textoActual = contenido.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(textoActual) || !string.IsNullOrWhiteSpace(titulo))
                    secciones.Add((titulo, textoActual));

                titulo = m.Groups[1].Value.Trim();
                contenido.Clear();
            }
            else
            {
                contenido.AppendLine(linea);
            }
        }

        var ultimo = contenido.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(ultimo))
            secciones.Add((titulo, ultimo));

        return secciones.Where(s => s.Item2.Length > 20).ToList();
    }

    /// <summary>Limpia el texto: colapsa líneas en blanco excesivas y normaliza espacios.</summary>
    private static string LimpiarTexto(string texto)
    {
        // Colapsa 3+ saltos de línea consecutivos a 2
        texto = Regex.Replace(texto, @"\n{3,}", "\n\n");
        // Elimina espacios al final de cada línea
        texto = Regex.Replace(texto, @"[ \t]+$", "", RegexOptions.Multiline);
        return texto.Trim();
    }

    /// <summary>Extrae palabras de más de 3 caracteres de la pregunta, en minúsculas.</summary>
    private static IReadOnlyList<string> ExtraerPalabras(string pregunta)
        => Regex.Matches(pregunta.ToLowerInvariant(), @"\b\w{4,}\b")
               .Select(m => m.Value)
               .Distinct()
               .ToList();

}
