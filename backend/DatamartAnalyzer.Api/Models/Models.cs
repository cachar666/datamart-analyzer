namespace DatamartAnalyzer.Api.Models;

// ─── Schema / Metadata ────────────────────────────────────────────────────────

public record SchemaColumn(
    string NombreTabla,
    string NombreCampo,
    string TipoCampo,
    string TipoObjeto,       // 'HECHO' | 'DIMENSION'
    string? DescripcionCampo,
    bool EsLlave,
    string? Formato
);

public record DatabaseInfo(string Nombre, bool EsDatamart);

public record ViewInfo(string Esquema, string NombreVista, string? Descripcion);

public record SchemaContext(
    string Database,
    List<SchemaColumn> Columnas,
    List<ViewInfo> Vistas
);

// ─── Analyze (AI-powered) ────────────────────────────────────────────────────

public record AnalyzeRequest(
    string Database,
    string Pregunta,
    List<SchemaColumn> Schema,
    List<ViewInfo> Vistas,
    string? HistorialContexto,  // JSON de mensajes anteriores para contexto
    string? Modelo              // Modelo de IA a usar (null = usa el default del servidor)
);

public enum TipoRespuesta
{
    Tabla,
    Grafico,
    Texto,
    TablaMasGrafico,
    TablaMasTexto,
    Error
}

public enum TipoGrafico
{
    Barras,
    Lineas,
    Torta,
    Area,
    Dispersion,
    Ninguno
}

public record UsageInfo(
    int TokensEntrada,
    int TokensSalida,
    int TokensCacheWrite,
    int TokensCacheRead,
    double CostoUsd,
    string Modelo
);

public record AnalyzeResponse(
    TipoRespuesta TipoRespuesta,
    string? ExplicacionTexto,
    string? SqlGenerado,
    List<Dictionary<string, object?>>? Datos,
    ConfiguracionGrafico? Grafico,
    string? MensajeError
)
{
    public bool EsPrebuilt { get; init; } = false;
    public UsageInfo? Usage { get; init; } = null;
}

public record ConfiguracionGrafico(
    TipoGrafico Tipo,
    string CampoEjeX,
    string CampoEjeY,
    string? CampoAgrupacion,
    string Titulo,
    string? ColorPrimario
);

// ─── Direct Query ─────────────────────────────────────────────────────────────

public record QueryRequest(
    string Database,
    string Sql
);

public record QueryResponse(
    bool Exitoso,
    List<Dictionary<string, object?>>? Datos,
    int TotalFilas,
    string? Error,
    double TiempoEjecucionMs
);

// ─── AI Raw Response (internal) ──────────────────────────────────────────────

public record AiRawResponse(
    string TipoRespuesta,
    string? ExplicacionTexto,
    string? SqlGenerado,
    AiGraficoConfig? Grafico
);

public record AiGraficoConfig(
    string Tipo,
    string CampoEjeX,
    string CampoEjeY,
    string? CampoAgrupacion,
    string Titulo,
    string? ColorPrimario
);
