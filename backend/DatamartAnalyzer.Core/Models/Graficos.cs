using System.Text.Json;
using System.Text.Json.Serialization;
using DatamartAnalyzer.Core.Models;

namespace DatamartAnalyzer.Core.Charts;

/// <summary>
/// Reglas de una configuracion de grafico.
///
/// Existe porque GraficoJson es una columna de texto libre y nadie validaba su
/// forma al guardar. Se encontraron dos maneras de terminar con un panel que dice
/// "TablaMasGrafico" y dibuja ejes vacios:
///
///   1. grafico en NULL — el panel se creo sin configuracion.
///   2. grafico con OTRO esquema — un {"tipo","ejeX","series":[...]} que al
///      deserializar en ConfiguracionGrafico deja CampoEjeX y CampoEjeY vacios.
///
/// Las dos pasaban sin ruido y reventaban recien al pintar, donde ya no hay forma
/// de saber que fue lo que fallo.
/// </summary>
public static class Graficos
{
    /// <summary>
    /// Una configuracion sin ejes no se puede dibujar. Devolver null es mejor que
    /// devolver un objeto a medias: el que dibuja sabe que no hay grafico, en vez de
    /// pintar un cuadro vacio.
    /// </summary>
    public static ConfiguracionGrafico? Normalizar(ConfiguracionGrafico? g)
    {
        if (g is null) return null;
        if (string.IsNullOrWhiteSpace(g.CampoEjeX) || string.IsNullOrWhiteSpace(g.CampoEjeY)) return null;
        if (g.Tipo == TipoGrafico.Ninguno) return null;
        return g;
    }

    /// <summary>
    /// ¿Se puede dibujar este grafico con estos datos? Se valida contra las columnas
    /// REALES del resultado, no contra lo que alguien creyo que devolvia la consulta.
    /// Un nombre de columna mal escrito es la forma mas facil de terminar con un
    /// grafico en blanco.
    /// </summary>
    public static bool Validar(
        ConfiguracionGrafico? g,
        IReadOnlyList<string> columnas,
        IReadOnlyDictionary<string, object?>? primeraFila,
        out string? motivo)
    {
        motivo = null;
        if (g is null) return true;   // sin grafico es valido: el panel es solo tabla

        if (string.IsNullOrWhiteSpace(g.CampoEjeX) || string.IsNullOrWhiteSpace(g.CampoEjeY))
        {
            motivo = "El grafico necesita campoEjeX y campoEjeY.";
            return false;
        }

        var disponibles = string.Join(", ", columnas);

        if (!columnas.Contains(g.CampoEjeX, StringComparer.OrdinalIgnoreCase))
        {
            motivo = $"La columna '{g.CampoEjeX}' (eje X) no esta en el resultado. Columnas: {disponibles}";
            return false;
        }

        if (!columnas.Contains(g.CampoEjeY, StringComparer.OrdinalIgnoreCase))
        {
            motivo = $"La columna '{g.CampoEjeY}' (eje Y) no esta en el resultado. Columnas: {disponibles}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(g.CampoAgrupacion) &&
            !columnas.Contains(g.CampoAgrupacion, StringComparer.OrdinalIgnoreCase))
        {
            motivo = $"La columna '{g.CampoAgrupacion}' (agrupacion) no esta en el resultado. Columnas: {disponibles}";
            return false;
        }

        // El eje Y se grafica como numero. Si trae texto, el grafico sale plano en
        // cero y nadie entiende por que.
        if (primeraFila is not null &&
            primeraFila.TryGetValue(ColumnaReal(columnas, g.CampoEjeY), out var v) &&
            v is not null && !EsNumerico(v))
        {
            motivo = $"La columna '{g.CampoEjeY}' del eje Y no es numerica (trae '{v}'). " +
                     "Elegi una columna de valores o agrega un COUNT/SUM en la consulta.";
            return false;
        }

        return true;
    }

    private static string ColumnaReal(IReadOnlyList<string> columnas, string pedida) =>
        columnas.FirstOrDefault(c => c.Equals(pedida, StringComparison.OrdinalIgnoreCase)) ?? pedida;

    private static bool EsNumerico(object v) =>
        v is byte or sbyte or short or ushort or int or uint or long or ulong
          or float or double or decimal;

    /// <summary>Arma la configuracion desde los datos sueltos que manda una herramienta.</summary>
    public static ConfiguracionGrafico? Construir(
        string? tipo, string? ejeX, string? ejeY, string? agrupacion, string? titulo, string? color)
    {
        if (string.IsNullOrWhiteSpace(tipo) &&
            string.IsNullOrWhiteSpace(ejeX) &&
            string.IsNullOrWhiteSpace(ejeY)) return null;

        return Normalizar(new ConfiguracionGrafico(
            ATipo(tipo), ejeX ?? "", ejeY ?? "",
            string.IsNullOrWhiteSpace(agrupacion) ? null : agrupacion,
            titulo ?? "",
            string.IsNullOrWhiteSpace(color) ? null : color));
    }

    /// <summary>
    /// El tipo llega escrito por quien sea: el modelo, la aplicacion web o un JSON
    /// viejo. "pie", "line" y "bar" son tan probables como "torta". Caer siempre en
    /// Barras es preferible a no dibujar, pero reconocer el sinonimo es mejor.
    /// </summary>
    private static TipoGrafico ATipo(string? tipo)
    {
        if (Enum.TryParse<TipoGrafico>(tipo, ignoreCase: true, out var t) && t != TipoGrafico.Ninguno)
            return t;

        return (tipo ?? "").Trim().ToLowerInvariant() switch
        {
            "pie" or "donut" or "doughnut" or "circular" => TipoGrafico.Torta,
            "line" or "linea" or "lineas" or "temporal" => TipoGrafico.Lineas,
            "scatter" or "puntos" => TipoGrafico.Dispersion,
            "stackedarea" or "areas" => TipoGrafico.Area,
            _ => TipoGrafico.Barras,
        };
    }

    // ── Lectura del JSON guardado ────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Lee la columna GraficoJson. Es texto libre, asi que en la practica guarda mas
    /// de una forma: la canonica (campoEjeX/campoEjeY) y la que escribio el modelo
    /// por su cuenta ({"tipo","ejeX","series":[{"campo"}]}). La segunda deserializaba
    /// "bien" y dejaba los dos ejes vacios — el panel decia TablaMasGrafico y dibujaba
    /// un recuadro en blanco.
    ///
    /// Rescatarla en vez de descartarla importa: los paneles ya guardados en los
    /// dashboards de los usuarios estan en esa forma, y nadie los va a rehacer.
    /// </summary>
    public static ConfiguracionGrafico? Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var canonica = Normalizar(JsonSerializer.Deserialize<ConfiguracionGrafico>(json, Json));
            if (canonica is not null) return canonica;
        }
        catch { /* no era la forma canonica; se intenta rescatar abajo */ }

        return Rescatar(json);
    }

    private static ConfiguracionGrafico? Rescatar(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;

            var tipo = Texto(raiz, "tipo", "type");
            var ejeX = Texto(raiz, "ejeX", "campoEjeX", "x", "xAxis", "categoria", "dimension");
            var ejeY = Texto(raiz, "ejeY", "campoEjeY", "y", "yAxis", "valor", "medida");
            var agrupacion = Texto(raiz, "agrupacion", "campoAgrupacion", "grupo", "serie");
            var titulo = Texto(raiz, "titulo", "title") ?? "";
            var color = Texto(raiz, "color", "colorPrimario");

            // series[] es la forma que mas aparece. Se toma la primera: el modelo de
            // grafico solo dibuja una, y media serie es mejor que ninguna.
            if (string.IsNullOrWhiteSpace(ejeY) &&
                raiz.TryGetProperty("series", out var series) &&
                series.ValueKind == JsonValueKind.Array && series.GetArrayLength() > 0)
            {
                var primera = series[0];
                if (primera.ValueKind == JsonValueKind.String)
                {
                    ejeY = primera.GetString();
                }
                else if (primera.ValueKind == JsonValueKind.Object)
                {
                    ejeY = Texto(primera, "campo", "field", "dataKey", "campoEjeY", "y");
                    if (string.IsNullOrWhiteSpace(titulo))
                        titulo = Texto(primera, "etiqueta", "label", "nombre") ?? "";
                }
            }

            return Construir(tipo, ejeX, ejeY, agrupacion, titulo, color);
        }
        catch { return null; }
    }

    /// <summary>El primer nombre que exista y traiga texto. JsonElement distingue mayusculas.</summary>
    private static string? Texto(JsonElement o, params string[] nombres)
    {
        foreach (var n in nombres)
        {
            if (!o.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.String) continue;
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }
}
