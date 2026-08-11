using System.Text.RegularExpressions;

namespace DatamartAnalyzer.Core.Sql;

/// <summary>
/// Inyecta los filtros de contexto en una consulta ya armada. Vive acá y no en un
/// controlador porque la usan la API, el conector MCP y los paneles de dashboard:
/// tres caminos, una sola regla.
///
/// Los prebuilt unen [ADP_DTM_DIM].[Proyecto] con alias p y, cuando aplica,
/// [ADP_DTM_DIM].[Insumo] con alias i.
///
///   empresa, macroproyecto, proyecto, estado → IN (...) sobre p
///   insumo                                   → LIKE '%texto%' sobre i
///
/// El filtro de insumo se IGNORA en silencio si la consulta no toca la dimension
/// Insumo. Es una decision de UX: que "listar proyectos con su estado" no falle
/// porque quedo un filtro de insumo activo de una consulta anterior.
/// </summary>
public static class FilterInjector
{
    // El mapa tipo → columna vive en FilterCatalog: lo comparten el inyector (que
    // arma el WHERE) y quien lista los valores disponibles.
    private static IReadOnlyDictionary<string, string> ColumnaPorFiltro => FilterCatalog.Columnas;

    private static readonly Regex UsaInsumo =
        new(@"\[?ADP_DTM_DIM\]?\.\[?Insumo\]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AliasInsumo =
        new(@"\[?ADP_DTM_DIM\]?\.\[?Insumo\]?\s+(?:AS\s+)?(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Inyectar(string sql, IReadOnlyDictionary<string, List<string>>? filtros)
    {
        if (filtros is null || filtros.Count == 0) return sql;

        var condiciones = new List<string>();

        foreach (var (tipo, valores) in filtros)
        {
            if (valores is null || valores.Count == 0) continue;

            if (string.Equals(tipo, "insumo", StringComparison.OrdinalIgnoreCase))
            {
                if (!UsaInsumo.IsMatch(sql)) continue;
                // Si quien genero el SQL ya puso la condicion, no se duplica.
                if (sql.Contains("[Insumo Descripcion] LIKE", StringComparison.OrdinalIgnoreCase)) continue;

                var alias = DetectarAliasInsumo(sql);
                foreach (var texto in valores)
                {
                    if (string.IsNullOrWhiteSpace(texto)) continue;
                    var esc = SqlSafety.EscaparParaLike(texto.Trim());
                    condiciones.Add($@"{alias}.[Insumo Descripcion] LIKE '%{esc}%' ESCAPE '\'");
                }
                continue;
            }

            if (!ColumnaPorFiltro.TryGetValue(tipo, out var col)) continue;

            var lista = string.Join(", ", valores.Select(v => $"'{SqlSafety.EscaparLiteral(v)}'"));
            condiciones.Add($"p.[{col}] IN ({lista})");
        }

        if (condiciones.Count == 0) return sql;

        var clausula = string.Join(" AND ", condiciones);

        var whereIdx = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIdx >= 0)
        {
            var pos = whereIdx + "WHERE".Length;
            return sql[..pos] + " " + clausula + " AND" + sql[pos..];
        }

        foreach (var kw in new[] { "GROUP BY", "ORDER BY", "HAVING" })
        {
            var idx = sql.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return sql[..idx] + $"WHERE {clausula}\n" + sql[idx..];
        }

        return sql + $"\nWHERE {clausula}";
    }

    /// <summary>¿La consulta toca la dimension Insumo?</summary>
    public static bool UsaDimensionInsumo(string sql) => UsaInsumo.IsMatch(sql);

    /// <summary>Alias con el que se unio Insumo; 'i' por convencion del proyecto.</summary>
    public static string DetectarAliasInsumo(string sql)
    {
        var m = AliasInsumo.Match(sql);
        if (!m.Success) return "i";

        var alias = m.Groups[1].Value;
        // "JOIN ADP_DTM_DIM.Insumo ON ..." — 'ON' no es un alias.
        return alias.Equals("ON", StringComparison.OrdinalIgnoreCase) ? "i" : alias;
    }
}
