using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.RegularExpressions;

namespace DatamartAnalyzer.Api.Services;

public interface IDocumentService
{
    string ObtenerContextoErp();
    string ObtenerSeccionesRelevantes(string pregunta, int maxChars = 3000);
    bool HayDocumento { get; }
}

public class DocumentService : IDocumentService
{
    private readonly string _textoCompleto;
    private readonly List<(string Titulo, string Contenido)> _secciones;
    private readonly ILogger<DocumentService> _logger;

    public bool HayDocumento => !string.IsNullOrWhiteSpace(_textoCompleto);

    public DocumentService(IConfiguration config, ILogger<DocumentService> logger)
    {
        _logger = logger;
        var ruta = config["Rag:DocumentPath"];

        if (string.IsNullOrWhiteSpace(ruta))
        {
            _logger.LogWarning("Rag:DocumentPath no configurado. El contexto ERP no estará disponible.");
            _textoCompleto = "";
            _secciones = [];
            return;
        }

        if (!File.Exists(ruta))
        {
            _logger.LogWarning("Documento RAG no encontrado en: {ruta}", ruta);
            _textoCompleto = "";
            _secciones = [];
            return;
        }

        try
        {
            var ext = Path.GetExtension(ruta).ToLowerInvariant();
            var textoRaw = ext is ".md" or ".txt" ? File.ReadAllText(ruta) : ExtraerTextoDocx(ruta);
            _textoCompleto = LimpiarTexto(textoRaw);
            _secciones = SplitEnSecciones(_textoCompleto);
            _logger.LogInformation(
                "Documento ERP cargado: {chars} chars, {secciones} secciones",
                _textoCompleto.Length, _secciones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo documento RAG: {ruta}", ruta);
            _textoCompleto = "";
            _secciones = [];
        }
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

        // Agregar secciones relevantes hasta el límite
        foreach (var (_, seccion, score) in puntuadas)
        {
            if (score == 0) break;
            if (seccion == intro) continue;   // ya incluida

            var texto = FormatSeccion(seccion);
            if (sb.Length + texto.Length > maxChars) break;
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

    private static string ExtraerTextoDocx(string ruta)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(ruta, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        foreach (var elemento in body.Elements())
        {
            if (elemento is Paragraph parrafo)
            {
                var texto = parrafo.InnerText.Trim();
                if (!string.IsNullOrEmpty(texto))
                    sb.AppendLine(texto);
            }
            else if (elemento is Table tabla)
            {
                foreach (var fila in tabla.Elements<TableRow>())
                {
                    var celdas = fila.Elements<TableCell>()
                        .Select(c => c.InnerText.Trim())
                        .Where(t => !string.IsNullOrEmpty(t));
                    sb.AppendLine(string.Join(" | ", celdas));
                }
            }
        }

        return sb.ToString();
    }
}
