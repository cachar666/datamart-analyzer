using System.Text.RegularExpressions;
using DatamartAnalyzer.Core.Models;

namespace DatamartAnalyzer.Core.Schema;

/// <summary>
/// Recorta el esquema a las tablas que la pregunta necesita.
///
/// Por que existe: el conector devolvia el esquema COMPLETO en cada pregunta sin
/// consulta preconstruida — 55.957 caracteres, 1.479 lineas. Claude tiene que leer
/// todo eso antes de escribir un solo SELECT, y para preguntar por costos no le
/// sirven las 200 columnas de inventario. Es tiempo de respuesta que el usuario
/// espera sin recibir nada a cambio.
///
/// La senal mas fuerte no es la pregunta: son las secciones del contexto ERP que
/// el RAG ya selecciono. Si el corpus trajo "ControlClaseOrigen - REGLA
/// FUNDAMENTAL", las tablas que ahi se nombran son las que hacen falta. La
/// pregunta se usa como refuerzo.
///
/// Nunca oculta que existen las demas: devuelve sus nombres, para que Claude sepa
/// que hay mas y pueda pedir el esquema completo con describir_datamart.
/// </summary>
public static class SchemaRelevance
{
    /// <summary>
    /// La dimension Proyecto entra siempre: los filtros de contexto (empresa,
    /// proyecto, macroproyecto, estado) se inyectan asumiendo que esta unida con
    /// alias p. Sin ella, un filtro activo produce SQL invalido.
    /// </summary>
    private const string TablaSiempre = "[ADP_DTM_DIM].[Proyecto]";

    private static readonly Regex NombreTabla =
        new(@"\[?(ADP_DTM_[A-Z]+)\]?\.\[?([A-Za-z0-9_ ]+)\]?", RegexOptions.Compiled);

    /// <summary>Palabras de 4+ letras, sin las vacias del español que no discriminan.</summary>
    private static readonly HashSet<string> Vacias = new(StringComparer.OrdinalIgnoreCase)
    {
        "cual", "cuales", "cuanto", "cuantos", "cuanta", "cuantas", "como", "donde",
        "para", "por", "con", "los", "las", "del", "que", "una", "uno", "cada",
        "sobre", "entre", "desde", "hasta", "mayor", "menor", "mostrar", "listar",
        "dame", "quiero", "tiene", "tienen", "esta", "estan", "este", "esta",
    };

    public sealed record Resultado(
        List<SchemaColumn> Columnas,
        List<string> TablasIncluidas,
        List<string> TablasOmitidas);

    /// <summary>
    /// Devuelve las columnas de las tablas relevantes y la lista de las que quedaron
    /// afuera. Si el esquema es chico no recorta nada: el costo de leerlo entero es
    /// menor que el riesgo de dejar afuera la tabla justa.
    /// </summary>
    public static Resultado Seleccionar(
        IReadOnlyList<SchemaColumn> esquema,
        string pregunta,
        string contextoErp,
        int maxTablas = 8)
    {
        var todas = esquema.Select(c => c.NombreTabla)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToList();

        if (todas.Count <= maxTablas)
            return new Resultado(esquema.ToList(), todas, []);

        // Tablas nombradas en las secciones del corpus que el RAG considero
        // relevantes. Es la senal mas confiable que hay.
        var enContexto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in NombreTabla.Matches(contextoErp ?? ""))
            enContexto.Add($"[{m.Groups[1].Value}].[{m.Groups[2].Value.Trim()}]");

        var palabras = ExtraerPalabras(pregunta);

        // OJO: la puntuacion NO incluye el bono de TablaSiempre. Si lo incluyera,
        // [Proyecto] siempre puntuaria y el fallback de mas abajo no se dispararia
        // nunca: una pregunta sobre proveedores devolvia un esquema con la sola
        // dimension Proyecto, con la que no se puede responder nada.
        var puntuadas = todas.Select(t =>
        {
            var hoja = Normalizar(HojaDe(t));
            var score = 0;

            if (enContexto.Contains(t)) score += 10;

            // El nombre de la tabla mencionado en la pregunta pesa mas que una columna.
            if (palabras.Any(p => hoja.Contains(p, StringComparison.OrdinalIgnoreCase))) score += 5;

            var columnas = esquema.Where(c => c.NombreTabla.Equals(t, StringComparison.OrdinalIgnoreCase));
            score += columnas.Count(c => palabras.Any(p =>
                        Normalizar(c.NombreCampo).Contains(p, StringComparison.OrdinalIgnoreCase)));

            return (Tabla: t, Score: score);
        })
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.Tabla, StringComparer.OrdinalIgnoreCase)
        .ToList();

        var conSenal = puntuadas.Where(x => x.Score > 0).Take(maxTablas).ToList();

        // Menos de dos tablas con senal propia: la pregunta no se parece a nada de
        // este datamart. Devolver el esquema completo cuesta tokens, pero entregar
        // uno recortado con el que no se puede responder cuesta una respuesta mala,
        // que es peor.
        if (conSenal.Count < 2)
            return new Resultado(esquema.ToList(), todas, []);

        var elegidas = conSenal.Select(x => x.Tabla).ToHashSet(StringComparer.OrdinalIgnoreCase);
        elegidas.Add(TablaSiempre);

        return new Resultado(
            esquema.Where(c => elegidas.Contains(c.NombreTabla)).ToList(),
            elegidas.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
            todas.Where(t => !elegidas.Contains(t))
                 .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string HojaDe(string tablaCompleta)
    {
        var i = tablaCompleta.LastIndexOf('.');
        return (i < 0 ? tablaCompleta : tablaCompleta[(i + 1)..]).Trim('[', ']');
    }

    /// <summary>
    /// Palabras de la pregunta, en singular y sin acentos.
    ///
    /// El singular no es un detalle: la gente pregunta por "proveedores" y la tabla
    /// se llama "Proveedor". Sin esto, esa pregunta no encontraba ni una tabla y se
    /// iba al esquema completo — o peor, a uno recortado inservible.
    /// </summary>
    private static List<string> ExtraerPalabras(string pregunta)
    {
        var crudas = Regex.Matches(pregunta ?? "", @"\b[\wáéíóúñÁÉÍÓÚÑ]{4,}\b")
                          .Select(m => Normalizar(m.Value))
                          .Where(p => !Vacias.Contains(p));

        var salida = new List<string>();
        foreach (var p in crudas)
        {
            salida.Add(p);
            // "proveedores" -> "proveedor";  "actas" -> "acta"
            if (p.EndsWith("es", StringComparison.OrdinalIgnoreCase) && p.Length > 5)
                salida.Add(p[..^2]);
            else if (p.EndsWith("s", StringComparison.OrdinalIgnoreCase) && p.Length > 4)
                salida.Add(p[..^1]);
        }

        return salida.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Quita acentos para que "análisis" y "analisis" sean la misma palabra.</summary>
    private static string Normalizar(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
