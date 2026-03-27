using DatamartAnalyzer.Api.Models;
using DatamartAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatamartAnalyzer.Api.Controllers;

// ─── Database Controller ──────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class DatabasesController : ControllerBase
{
    private readonly ISqlServerService _sql;

    public DatabasesController(ISqlServerService sql) => _sql = sql;

    [HttpGet]
    public async Task<IActionResult> GetDatabases()
    {
        try
        {
            var dbs = await _sql.ObtenerBasesDatosAsync();
            return Ok(new
            {
                databases = dbs,
                ultimaCarga = _sql.UltimaCargaBases,
                desdeCahe = true
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            var dbs = await _sql.RefrescarBasesDatosAsync();
            return Ok(new
            {
                databases = dbs,
                ultimaCarga = _sql.UltimaCargaBases,
                desdeCache = false
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        var ok = await _sql.ProbarConexionAsync();
        return Ok(new { connected = ok, server = "YOSEMITE" });
    }
}

// ─── Schema Controller ────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class SchemaController : ControllerBase
{
    private readonly ISchemaService _schema;
    private readonly ISqlServerService _sql;

    public SchemaController(ISchemaService schema, ISqlServerService sql)
    {
        _schema = schema;
        _sql = sql;
    }

    [HttpGet("{database}")]
    public async Task<IActionResult> GetSchema(string database)
    {
        try
        {
            var existe = await _sql.ExisteBaseDatosAsync(database);
            if (!existe)
                return NotFound(new
                {
                    error = $"La base de datos '{database}' no existe o no está disponible en el servidor YOSEMITE.",
                    recomendacion = "Usa el botón de refresco en el listado de bases de datos para actualizar la lista disponible."
                });

            var contexto = await _schema.ObtenerContextoCompletoAsync(database);
            return Ok(contexto);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{database}/views")]
    public async Task<IActionResult> GetViews(string database)
    {
        try
        {
            var vistas = await _schema.ObtenerVistasAsync(database);
            return Ok(vistas);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

// ─── Analyze Controller ───────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class AnalyzeController : ControllerBase
{
    private readonly IAnthropicService _ai;
    private readonly ISqlServerService _sql;
    private readonly ISchemaService _schema;
    private readonly IPrebuiltQueriesService _prebuilt;
    private readonly ILogger<AnalyzeController> _logger;

    public AnalyzeController(
        IAnthropicService ai,
        ISqlServerService sql,
        ISchemaService schema,
        IPrebuiltQueriesService prebuilt,
        ILogger<AnalyzeController> logger)
    {
        _ai = ai;
        _sql = sql;
        _schema = schema;
        _prebuilt = prebuilt;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Database))
            return BadRequest(new { error = "Se requiere una base de datos." });

        if (string.IsNullOrWhiteSpace(request.Pregunta))
            return BadRequest(new { error = "Se requiere una pregunta." });

        try
        {
            // ── Verificar si hay query preconstruida (sin costo de IA) ─────────
            var prebuiltMatch = _prebuilt.Match(request.Pregunta);
            if (prebuiltMatch is not null)
            {
                _logger.LogInformation("Query preconstruida encontrada para: {pregunta}", request.Pregunta);
                var queryResult = await _sql.EjecutarQueryAsync(request.Database, prebuiltMatch.Sql);
                if (queryResult.Exitoso)
                {
                    return Ok(new AnalyzeResponse(
                        TipoRespuesta: prebuiltMatch.TipoRespuesta,
                        ExplicacionTexto: prebuiltMatch.ExplicacionTexto,
                        SqlGenerado: prebuiltMatch.Sql,
                        Datos: queryResult.Datos,
                        Grafico: prebuiltMatch.Grafico,
                        MensajeError: null
                    ) { EsPrebuilt = true });
                }
                _logger.LogWarning("Query preconstruida falló, fallback a IA. Error: {error}", queryResult.Error);
            }
            // ────────────────────────────────────────────────────────────────────
            // Si el schema no viene en el request, cargarlo
            var schema = request.Schema?.Any() == true
                ? request.Schema
                : await _schema.ObtenerSchemaAsync(request.Database);

            var vistas = request.Vistas?.Any() == true
                ? request.Vistas
                : await _schema.ObtenerVistasAsync(request.Database);

            var enrichedRequest = request with
            {
                Schema = schema,
                Vistas = vistas
            };

            // 1. IA genera el plan de respuesta
            AiRawResponse aiResponse;
            UsageInfo? usageInfo = null;
            try
            {
                (aiResponse, usageInfo) = await _ai.AnalizarPreguntaAsync(enrichedRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error llamando a Anthropic API");
                return StatusCode(500, new { error = $"Error de IA: {ex.Message}" });
            }

            // 2. Si hay SQL, ejecutarlo (con un retry automático si falla por columnas inválidas)
            List<Dictionary<string, object?>>? datos = null;
            if (!string.IsNullOrWhiteSpace(aiResponse.SqlGenerado))
            {
                var queryResult = await _sql.EjecutarQueryAsync(request.Database, aiResponse.SqlGenerado);

                if (!queryResult.Exitoso && EsErrorDeColumna(queryResult.Error))
                {
                    _logger.LogWarning("SQL falló por columna inválida, reintentando con corrección. Error: {error}", queryResult.Error);
                    try
                    {
                        UsageInfo? retryUsage;
                        (aiResponse, retryUsage) = await _ai.CorregirSqlAsync(enrichedRequest, aiResponse.SqlGenerado, queryResult.Error!);
                        if (retryUsage is not null)
                            usageInfo = usageInfo is null ? retryUsage : usageInfo with
                            {
                                TokensEntrada    = usageInfo.TokensEntrada    + retryUsage.TokensEntrada,
                                TokensSalida     = usageInfo.TokensSalida     + retryUsage.TokensSalida,
                                TokensCacheWrite = usageInfo.TokensCacheWrite + retryUsage.TokensCacheWrite,
                                TokensCacheRead  = usageInfo.TokensCacheRead  + retryUsage.TokensCacheRead,
                                CostoUsd         = usageInfo.CostoUsd         + retryUsage.CostoUsd
                            };

                        if (!string.IsNullOrWhiteSpace(aiResponse.SqlGenerado))
                        {
                            queryResult = await _sql.EjecutarQueryAsync(request.Database, aiResponse.SqlGenerado);
                        }
                        else
                        {
                            queryResult = new QueryResponse(true, null, 0, null, 0);
                        }
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "Error en retry de corrección SQL");
                    }
                }

                if (!queryResult.Exitoso)
                {
                    return Ok(new AnalyzeResponse(
                        TipoRespuesta.Error,
                        $"Error ejecutando consulta: {queryResult.Error}\n\nSQL generado:\n{aiResponse.SqlGenerado}",
                        aiResponse.SqlGenerado,
                        null,
                        null,
                        queryResult.Error
                    ));
                }
                datos = queryResult.Datos;
            }

            // 3. Mapear respuesta
            var tipoRespuesta = aiResponse.TipoRespuesta?.ToLower() switch
            {
                "tabla" => TipoRespuesta.Tabla,
                "grafico" => TipoRespuesta.Grafico,
                "texto" => TipoRespuesta.Texto,
                "tablamasgrafico" or "tabla_mas_grafico" => TipoRespuesta.TablaMasGrafico,
                "tablasmastexto" or "tabla_mas_texto" => TipoRespuesta.TablaMasTexto,
                _ => TipoRespuesta.Texto
            };

            ConfiguracionGrafico? grafico = null;
            if (aiResponse.Grafico is not null)
            {
                var tipoGraf = aiResponse.Grafico.Tipo?.ToLower() switch
                {
                    "barras" => TipoGrafico.Barras,
                    "lineas" => TipoGrafico.Lineas,
                    "torta" or "pie" => TipoGrafico.Torta,
                    "area" => TipoGrafico.Area,
                    "dispersion" or "scatter" => TipoGrafico.Dispersion,
                    _ => TipoGrafico.Barras
                };

                grafico = new ConfiguracionGrafico(
                    Tipo: tipoGraf,
                    CampoEjeX: aiResponse.Grafico.CampoEjeX,
                    CampoEjeY: aiResponse.Grafico.CampoEjeY,
                    CampoAgrupacion: aiResponse.Grafico.CampoAgrupacion,
                    Titulo: aiResponse.Grafico.Titulo,
                    ColorPrimario: aiResponse.Grafico.ColorPrimario ?? "#3b82f6"
                );
            }

            return Ok(new AnalyzeResponse(
                TipoRespuesta: tipoRespuesta,
                ExplicacionTexto: aiResponse.ExplicacionTexto,
                SqlGenerado: aiResponse.SqlGenerado,
                Datos: datos,
                Grafico: grafico,
                MensajeError: null
            ) { Usage = usageInfo });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado en Analyze");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static bool EsErrorDeColumna(string? error) =>
        error != null && (
            error.Contains("nombre de columna") ||
            error.Contains("column name") ||
            error.Contains("Invalid column") ||
            error.Contains("no es válido")
        );
}

// ─── Query Controller (Direct SQL mode) ──────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class QueryController : ControllerBase
{
    private readonly ISqlServerService _sql;

    public QueryController(ISqlServerService sql) => _sql = sql;

    [HttpPost]
    public async Task<IActionResult> Execute([FromBody] QueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Database) || string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { error = "Se requieren database y sql." });

        var result = await _sql.EjecutarQueryAsync(request.Database, request.Sql);
        return Ok(result);
    }
}
