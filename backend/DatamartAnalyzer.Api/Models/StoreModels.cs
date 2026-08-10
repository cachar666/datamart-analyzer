namespace DatamartAnalyzer.Api.Models;

// ═══════════════════════════════════════════════════════════════════════════════
//  Almacén de consultas guardadas, dashboards y presets de filtros.
//
//  Vive DENTRO de cada base de datamart, en el esquema ADP_DTM_APP. No existe una
//  base central: el datamart con el que estás trabajando ES el contexto.
//
//  Por eso el nombre de la base viaja en la RUTA (/api/savedqueries/{database}/...)
//  y no en el cuerpo. En los DTOs de respuesta sí aparece Database, para que el
//  cliente sepa de dónde salió cada cosa.
// ═══════════════════════════════════════════════════════════════════════════════

public record StoreStatusDto(
    string Database,
    bool Instalado,
    int Consultas,
    int Dashboards,
    int Paneles,
    string? Mensaje
);

// ─── Consultas guardadas ──────────────────────────────────────────────────────

public record SavedQueryDto(
    Guid Id,
    string Database,
    string Pregunta,
    string Sql,
    TipoRespuesta TipoRespuesta,
    ConfiguracionGrafico? Grafico,
    string? Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy
);

public record CreateSavedQueryRequest(
    string Pregunta,
    string Sql,
    TipoRespuesta TipoRespuesta = TipoRespuesta.Tabla,
    ConfiguracionGrafico? Grafico = null,
    string? Tags = null
);

/// <summary>Actualización parcial: los campos en null se dejan como están.</summary>
public record UpdateSavedQueryRequest(
    string? Pregunta = null,
    string? Sql = null,
    TipoRespuesta? TipoRespuesta = null,
    ConfiguracionGrafico? Grafico = null,
    string? Tags = null
);

// ─── Dashboards ───────────────────────────────────────────────────────────────

public record DashboardDto(
    Guid Id,
    string Nombre,
    string Database,
    string? Descripcion,
    int TotalPaneles,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy
);

public record DashboardDetailDto(
    Guid Id,
    string Nombre,
    string Database,
    string? Descripcion,
    List<PanelDto> Paneles,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy
);

public record PanelDto(
    Guid Id,
    Guid DashboardId,
    string Titulo,
    string Sql,
    string SqlBase,
    bool EsPrebuilt,
    string? PrebuiltKey,
    Dictionary<string, List<string>>? FiltrosFijos,
    bool AceptaFiltrosGlobales,
    TipoRespuesta TipoRespuesta,
    ConfiguracionGrafico? Grafico,
    int Orden
);

public record CreateDashboardRequest(
    string Nombre,
    string? Descripcion = null
);

public record UpdateDashboardRequest(
    string? Nombre = null,
    string? Descripcion = null
);

/// <summary>
/// Agrega un panel desde una consulta guardada (SavedQueryId) o desde SQL directo (Sql).
/// Debe venir exactamente uno de los dos.
/// </summary>
public record AddPanelRequest(
    string Titulo,
    Guid? SavedQueryId = null,
    string? Sql = null,
    TipoRespuesta TipoRespuesta = TipoRespuesta.Tabla,
    ConfiguracionGrafico? Grafico = null,
    bool EsPrebuilt = false,
    string? PrebuiltKey = null,
    Dictionary<string, List<string>>? FiltrosFijos = null,
    bool AceptaFiltrosGlobales = true
);

public record UpdatePanelRequest(
    string? Titulo = null,
    bool? AceptaFiltrosGlobales = null,
    Dictionary<string, List<string>>? FiltrosFijos = null
);

public record ReorderPanelsRequest(
    List<Guid> PanelIds
);

// ─── Presets de filtros ───────────────────────────────────────────────────────

public record FilterPresetDto(
    Guid Id,
    string Nombre,
    string Database,
    Dictionary<string, List<string>> Filtros,
    DateTime CreatedAt,
    string CreatedBy
);

public record CreateFilterPresetRequest(
    string Nombre,
    Dictionary<string, List<string>> Filtros
);

// ─── Importación desde localStorage ───────────────────────────────────────────

public record ImportRequest(
    List<ImportedQuery>? Consultas = null,
    List<ImportedDashboard>? Dashboards = null
);

public record ImportedQuery(
    string Pregunta,
    string Sql,
    string? TipoRespuesta = null,
    ConfiguracionGrafico? Grafico = null
);

public record ImportedDashboard(
    string Nombre,
    List<ImportedPanel> Paneles
);

public record ImportedPanel(
    string Titulo,
    string Sql,
    string? SqlBase = null,
    string? TipoRespuesta = null,
    ConfiguracionGrafico? Grafico = null,
    bool EsPrebuilt = false,
    string? PrebuiltKey = null,
    Dictionary<string, List<string>>? FiltrosFijos = null,
    bool AceptaFiltrosGlobales = true
);

public record ImportResultDto(
    int ConsultasImportadas,
    int ConsultasOmitidas,
    int DashboardsImportados,
    int DashboardsOmitidos,
    int PanelesImportados,
    List<string> Advertencias
);

// ─── Contexto del ERP ─────────────────────────────────────────────────────────

/// <summary>
/// ERP desde el que se abrió la app y el datamart que declara en dbo.ADPConfig.
/// El ERP puede ser externo y su datamart vivir en otro servidor, por eso viajan
/// ambos servidores. NUNCA incluye la cadena de conexión: puede traer credenciales.
/// DesdeErp = false → modo standalone: se ven todos los datamarts, como hasta ahora.
/// </summary>
public record ErpContextDto(
    bool DesdeErp,
    string? ErpServidor,
    string? ErpDatabase,
    string? DatamartServidor,
    string? Datamart,
    bool DatamartExiste,
    string? Mensaje
);
