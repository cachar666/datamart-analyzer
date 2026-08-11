namespace DatamartAnalyzer.Core.Sql;

/// <summary>
/// Que columna de la dimension Proyecto hay detras de cada filtro conocido.
///
/// Este mapa estaba escrito dos veces: en el inyector de filtros (que arma el
/// WHERE) y en el controlador que lista los valores disponibles. Dos copias de la
/// misma tabla es una invitacion a que un dia el desplegable ofrezca valores de
/// una columna y el WHERE filtre por otra. Una sola.
/// </summary>
public static class FilterCatalog
{
    /// <summary>Tabla de la dimension donde viven todos los filtros conocidos.</summary>
    public const string TablaProyecto = "[ADP_DTM_DIM].[Proyecto]";

    /// <summary>tipo de filtro → nombre de columna (sin corchetes).</summary>
    public static readonly IReadOnlyDictionary<string, string> Columnas =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["proyecto"] = "Nombre Proyecto",
            ["macroproyecto"] = "MacroProyecto",
            ["empresa"] = "Empresa",
            ["estado"] = "Estado",
        };

    /// <summary>Los tipos que se pueden pedir, para mensajes de error utiles.</summary>
    public static string TiposConocidos => string.Join(", ", Columnas.Keys);

    /// <summary>
    /// El filtro de insumo no vive en Proyecto: es texto libre contra
    /// [ADP_DTM_DIM].[Insumo].[Insumo Descripcion] y se resuelve con LIKE.
    /// </summary>
    public const string FiltroInsumo = "insumo";

    public static bool EsFiltroDeProyecto(string tipo) => Columnas.ContainsKey(tipo);
}
