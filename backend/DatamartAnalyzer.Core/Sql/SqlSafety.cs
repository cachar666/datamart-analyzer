using System.Text.RegularExpressions;

namespace DatamartAnalyzer.Core.Sql;

/// <summary>
/// Barrera de seguridad para el SQL que llega de afuera — de la IA, del conector
/// MCP o escrito a mano. UNA sola implementacion: si la API y el conector tuvieran
/// cada uno la suya, un agujero tapado en una seguiria abierto en la otra.
///
/// Solo lectura: SELECT y WITH (CTEs). Nada de DML ni DDL.
/// </summary>
public static class SqlSafety
{
    /// <summary>
    /// Palabras que no pueden aparecer en el SQL. Se buscan como palabra completa:
    /// asi 'CREATED' o una columna llamada [Update] no disparan un falso positivo,
    /// pero 'DROP TABLE' si.
    /// </summary>
    private static readonly string[] Prohibidas =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "TRUNCATE", "EXEC", "EXECUTE", "MERGE", "GRANT", "REVOKE",
        "DENY", "BACKUP", "RESTORE", "RECONFIGURE", "SHUTDOWN",

        // SELECT ... INTO NuevaTabla escribe: empieza por SELECT y pasaba el filtro.
        "INTO",

        // WAITFOR DELAY convierte una consulta en una negacion de servicio barata.
        "WAITFOR",

        // Leen fuera del datamart (archivos, servidores vinculados).
        "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "OPENXML",
    ];

    private static readonly Regex Sp = new(@"\bsp_\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Xp = new(@"\bxp_\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// ¿Es seguro ejecutar este SQL? Devuelve el motivo cuando no lo es, para que
    /// el llamador pueda explicarlo en vez de decir solo "no permitido".
    /// </summary>
    public static bool EsSeguro(string sql, out string? motivo)
    {
        motivo = null;

        if (string.IsNullOrWhiteSpace(sql))
        {
            motivo = "La consulta esta vacia.";
            return false;
        }

        var limpio = SinComentarios(sql).Trim();

        if (!limpio.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !limpio.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            motivo = "Solo se permiten consultas SELECT (o WITH para CTEs).";
            return false;
        }

        foreach (var kw in Prohibidas)
        {
            if (Regex.IsMatch(limpio, $@"\b{kw}\b", RegexOptions.IgnoreCase))
            {
                motivo = $"Operacion no permitida: {kw}.";
                return false;
            }
        }

        if (Sp.IsMatch(limpio) || Xp.IsMatch(limpio))
        {
            motivo = "No se permiten procedimientos del sistema (sp_ / xp_).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Agrega TOP n si la consulta no acota filas. Respeta DISTINCT: TOP va DESPUES,
    /// nunca entre SELECT y DISTINCT, que es sintaxis invalida.
    /// </summary>
    public static string LimitarFilas(string sql, int maxFilas)
    {
        var s = sql.Trim();

        if (Regex.IsMatch(s, @"\bTOP\s*\(?\s*\d", RegexOptions.IgnoreCase)) return s;
        if (Regex.IsMatch(s, @"\bFETCH\s+NEXT\b", RegexOptions.IgnoreCase)) return s;

        // En un WITH el SELECT final puede estar mucho mas abajo; no se toca.
        if (s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)) return s;

        var m = Regex.Match(s, @"^\s*SELECT\s+(DISTINCT\s+)?", RegexOptions.IgnoreCase);
        if (!m.Success) return s;

        return s.Insert(m.Length, $"TOP {maxFilas} ");
    }

    /// <summary>
    /// Quita comentarios antes de analizar. Sin esto, un -- o un bloque /* */ alcanzan
    /// para esconder una palabra prohibida del chequeo.
    /// </summary>
    private static string SinComentarios(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        sql = Regex.Replace(sql, @"--[^\r\n]*", " ");
        return sql;
    }

    /// <summary>Escapa un literal para usarlo dentro de comillas simples en T-SQL.</summary>
    public static string EscaparLiteral(string valor) => valor.Replace("'", "''");

    /// <summary>
    /// Escapa para LIKE. El orden importa: la barra invertida va PRIMERO, si no se
    /// termina escapando el escape. Se emite con ESCAPE '\'.
    /// </summary>
    public static string EscaparParaLike(string valor) =>
        valor.Replace("\\", "\\\\")
             .Replace("%", "\\%")
             .Replace("_", "\\_")
             .Replace("'", "''");
}
