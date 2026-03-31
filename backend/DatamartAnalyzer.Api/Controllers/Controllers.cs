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
            _logger.LogInformation("ANALYZE recibido | BD:{db} | filtros:{f} | pregunta:{p}",
                request.Database,
                request.ContextoVariables is { Count: > 0 }
                    ? string.Join("; ", request.ContextoVariables.Select(kv => $"{kv.Key}=[{string.Join(",", kv.Value)}]"))
                    : "NINGUNO",
                request.Pregunta[..Math.Min(60, request.Pregunta.Length)]);

            // ── Verificar si hay query preconstruida (sin costo de IA) ─────────
            var prebuiltMatch = _prebuilt.Match(request.Pregunta);
            if (prebuiltMatch is not null)
            {
                var sqlConFiltros = InjectarFiltrosPrebuilt(prebuiltMatch.Sql, request.ContextoVariables);
                _logger.LogInformation("Query preconstruida para: {pregunta} | filtros: {f}",
                    request.Pregunta,
                    request.ContextoVariables is { Count: > 0 } ? string.Join(", ", request.ContextoVariables.Keys) : "ninguno");
                var queryResult = await _sql.EjecutarQueryAsync(request.Database, sqlConFiltros);
                if (queryResult.Exitoso)
                {
                    return Ok(new AnalyzeResponse(
                        TipoRespuesta: prebuiltMatch.TipoRespuesta,
                        ExplicacionTexto: prebuiltMatch.ExplicacionTexto,
                        SqlGenerado: sqlConFiltros,
                        Datos: queryResult.Datos,
                        Grafico: prebuiltMatch.Grafico,
                        MensajeError: null
                    ) { EsPrebuilt = true });
                }
                // Prebuilt falló: devolver error sin pasar a IA (las prebuilt nunca deben usar IA)
                _logger.LogWarning("Query preconstruida falló: {error}", queryResult.Error);
                return Ok(new AnalyzeResponse(
                    TipoRespuesta: TipoRespuesta.Error,
                    ExplicacionTexto: null,
                    SqlGenerado: sqlConFiltros,
                    Datos: null,
                    Grafico: null,
                    MensajeError: $"Error ejecutando consulta: {queryResult.Error}"
                ) { EsPrebuilt = true });
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

    /// <summary>
    /// Inyecta filtros de contexto (proyecto, macroproyecto, empresa) en el SQL pre-construido
    /// sin usar IA. Los prebuilt siempre hacen JOIN a [ADP_DTM_DIM].[Proyecto] con alias "p".
    /// </summary>
    private static string InjectarFiltrosPrebuilt(string sql, Dictionary<string, List<string>>? filtros)
    {
        if (filtros is null || filtros.Count == 0) return sql;

        // Mapeo fijo: clave de filtro → columna en la dimensión Proyecto (alias p)
        var mapColumna = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["proyecto"]      = "Nombre Proyecto",
            ["macroproyecto"] = "MacroProyecto",
            ["empresa"]       = "Empresa",
            ["estado"]        = "Estado",
        };

        var condiciones = new List<string>();
        foreach (var (tipo, valores) in filtros)
        {
            if (valores is null || valores.Count == 0) continue;
            if (!mapColumna.TryGetValue(tipo, out var col)) continue;
            var lista = string.Join(", ", valores.Select(v => $"'{v.Replace("'", "''")}'"));
            condiciones.Add($"p.[{col}] IN ({lista})");
        }

        if (condiciones.Count == 0) return sql;

        var clausula = string.Join(" AND ", condiciones);

        // Si ya hay un WHERE, agregar con AND
        var whereIdx = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIdx >= 0)
        {
            // Insertar después del WHERE token y su condición existente
            var insertPos = whereIdx + "WHERE".Length;
            return sql[..insertPos] + " " + clausula + " AND" + sql[insertPos..];
        }

        // Si no hay WHERE, insertar antes del primer GROUP BY u ORDER BY
        foreach (var keyword in new[] { "GROUP BY", "ORDER BY", "HAVING" })
        {
            var idx = sql.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return sql[..idx] + $"WHERE {clausula}\n                    " + sql[idx..];
        }

        // Sin GROUP BY ni ORDER BY: agregar al final
        return sql + $"\nWHERE {clausula}";
    }

    /// <summary>
    /// Ejecuta un SQL con inyección de filtros — usado por los paneles del dashboard.
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteWithFilters([FromBody] ExecuteWithFiltersRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Database) || string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { error = "Se requieren database y sql." });

        var sqlConFiltros = InjectarFiltrosPrebuilt(request.Sql, request.Filtros);
        var result = await _sql.EjecutarQueryAsync(request.Database, sqlConFiltros);
        return Ok(result);
    }

    private static bool EsErrorDeColumna(string? error) =>
        error != null && (
            error.Contains("nombre de columna") ||
            error.Contains("column name") ||
            error.Contains("Invalid column") ||
            error.Contains("no es válido")
        );
}

// ─── Filters Controller ───────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class FiltersController : ControllerBase
{
    private readonly ISchemaService _schema;
    private readonly ISqlServerService _sql;

    public FiltersController(ISchemaService schema, ISqlServerService sql)
    {
        _schema = schema;
        _sql = sql;
    }

    /// <summary>
    /// Devuelve los valores distintos de una dimensión (empresa, proyecto, macroproyecto, etc.)
    /// descubriendo automáticamente la tabla y columna desde los metadatos del schema.
    /// </summary>
    [HttpGet("{database}/{tipo}")]
    public async Task<IActionResult> GetFilterValues(string database, string tipo)
    {
        // Validar tipo (solo alfanumérico + guion/barra baja)
        if (!System.Text.RegularExpressions.Regex.IsMatch(tipo, @"^[a-zA-Z0-9_\-]{1,50}$"))
            return BadRequest(new { error = "Tipo de filtro inválido." });

        try
        {
            // Mapeo explícito para filtros conocidos que viven en [ADP_DTM_DIM].[Proyecto]
            var mapeoExplicito = new Dictionary<string, (string Tabla, string Columna)>(StringComparer.OrdinalIgnoreCase)
            {
                ["proyecto"]      = ("[ADP_DTM_DIM].[Proyecto]", "Nombre Proyecto"),
                ["macroproyecto"] = ("[ADP_DTM_DIM].[Proyecto]", "MacroProyecto"),
                ["empresa"]       = ("[ADP_DTM_DIM].[Proyecto]", "Empresa"),
                ["estado"]        = ("[ADP_DTM_DIM].[Proyecto]", "Estado"),
            };

            if (mapeoExplicito.TryGetValue(tipo, out var mapeo))
            {
                var esMacro     = tipo.Equals("macroproyecto", StringComparison.OrdinalIgnoreCase);
                var esProyecto  = tipo.Equals("proyecto",      StringComparison.OrdinalIgnoreCase);

                // Macroproyecto: traer Empresa para subtext/tooltip
                if (esMacro)
                {
                    var sql = $"SELECT [{mapeo.Columna}], [Empresa] " +
                              $"FROM {mapeo.Tabla} " +
                              $"WHERE [{mapeo.Columna}] IS NOT NULL AND [{mapeo.Columna}] <> '' " +
                              $"ORDER BY [{mapeo.Columna}]";
                    var res = await _sql.EjecutarQueryAsync(database, sql);
                    if (!res.Exitoso) return StatusCode(500, new { error = res.Error });

                    var valores = res.Datos!
                        .Select(r => r.TryGetValue(mapeo.Columna, out var v) ? v?.ToString() : null)
                        .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v).ToList();

                    var empresaPorValor = new Dictionary<string, List<string>>();
                    foreach (var row in res.Datos!)
                    {
                        var val = row.TryGetValue(mapeo.Columna, out var v) ? v?.ToString() : null;
                        var emp = row.TryGetValue("Empresa", out var e) ? e?.ToString() : null;
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        if (!empresaPorValor.ContainsKey(val)) empresaPorValor[val] = [];
                        if (!string.IsNullOrWhiteSpace(emp) && !empresaPorValor[val].Contains(emp))
                            empresaPorValor[val].Add(emp);
                    }
                    return Ok(new { tipo, valores, tabla = mapeo.Tabla, columna = mapeo.Columna, empresaPorValor });
                }

                // Proyecto: traer Codigo, Empresa, MacroProyecto para subtext/tooltip
                if (esProyecto)
                {
                    var sql = $"SELECT [{mapeo.Columna}], MIN([Codigo Proyecto]) AS Codigo, " +
                              $"MIN([Empresa]) AS Empresa, MIN([MacroProyecto]) AS MacroProyecto " +
                              $"FROM {mapeo.Tabla} " +
                              $"WHERE [{mapeo.Columna}] IS NOT NULL AND [{mapeo.Columna}] <> '' " +
                              $"GROUP BY [{mapeo.Columna}] " +
                              $"ORDER BY [{mapeo.Columna}]";
                    var res = await _sql.EjecutarQueryAsync(database, sql);
                    if (!res.Exitoso) return StatusCode(500, new { error = res.Error });

                    var valores = res.Datos!
                        .Select(r => r.TryGetValue(mapeo.Columna, out var v) ? v?.ToString() : null)
                        .Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

                    var metadataPorValor = new Dictionary<string, object>();
                    foreach (var row in res.Datos!)
                    {
                        var val    = row.TryGetValue(mapeo.Columna,  out var v)  ? v?.ToString()  : null;
                        var codigo = row.TryGetValue("Codigo",        out var c)  ? c?.ToString()  : null;
                        var emp    = row.TryGetValue("Empresa",        out var e)  ? e?.ToString()  : null;
                        var macro  = row.TryGetValue("MacroProyecto", out var m)  ? m?.ToString()  : null;
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        metadataPorValor[val] = new { codigo, empresa = emp, macroproyecto = macro };
                    }
                    return Ok(new { tipo, valores, tabla = mapeo.Tabla, columna = mapeo.Columna, metadataPorValor });
                }

                // Otros tipos: simple DISTINCT
                var sqlSimple = $"SELECT DISTINCT [{mapeo.Columna}] FROM {mapeo.Tabla} " +
                                $"WHERE [{mapeo.Columna}] IS NOT NULL ORDER BY [{mapeo.Columna}]";
                var resSimple = await _sql.EjecutarQueryAsync(database, sqlSimple);
                if (!resSimple.Exitoso) return StatusCode(500, new { error = resSimple.Error });

                var valoresSimple = resSimple.Datos?
                    .Select(row => row.TryGetValue(mapeo.Columna, out var v) ? v?.ToString() : null)
                    .Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];

                return Ok(new { tipo, valores = valoresSimple, tabla = mapeo.Tabla, columna = mapeo.Columna });
            }

            var columns = await _schema.ObtenerSchemaAsync(database);

            var keyword = tipo.ToLowerInvariant();

            // 1) Buscar tabla cuyo nombre contenga el keyword
            var matchingGroup = columns
                .Where(c => c.TipoObjeto.Equals("DIMENSION", StringComparison.OrdinalIgnoreCase)
                         && c.NombreTabla.ToLowerInvariant().Contains(keyword))
                .GroupBy(c => c.NombreTabla)
                .FirstOrDefault();

            // 2) Si no hay tabla con ese nombre, buscar columna que contenga el keyword en dimensiones
            SchemaColumn? displayCol;
            if (matchingGroup != null)
            {
                displayCol = matchingGroup
                    .Where(c => !c.EsLlave &&
                           (c.TipoCampo.Contains("varchar", StringComparison.OrdinalIgnoreCase) ||
                            c.TipoCampo.Contains("char", StringComparison.OrdinalIgnoreCase) ||
                            c.TipoCampo.Contains("nchar", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(c => c.NombreCampo.Contains("nombre", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(c => c.NombreCampo.Contains("name", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
            }
            else
            {
                // Buscar por nombre de columna que contenga el keyword
                displayCol = columns
                    .Where(c => c.TipoObjeto.Equals("DIMENSION", StringComparison.OrdinalIgnoreCase)
                             && !c.EsLlave
                             && c.NombreCampo.ToLowerInvariant().Contains(keyword)
                             && (c.TipoCampo.Contains("varchar", StringComparison.OrdinalIgnoreCase) ||
                                 c.TipoCampo.Contains("char", StringComparison.OrdinalIgnoreCase) ||
                                 c.TipoCampo.Contains("nchar", StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault();
            }

            if (displayCol == null)
                return NotFound(new { error = $"No se encontró dimensión para '{tipo}'." });

            // NombreTabla ya viene como [esquema].[tabla], NombreCampo sin brackets
            var sql = $"SELECT DISTINCT [{displayCol.NombreCampo}] " +
                      $"FROM {displayCol.NombreTabla} " +
                      $"WHERE [{displayCol.NombreCampo}] IS NOT NULL " +
                      $"ORDER BY [{displayCol.NombreCampo}]";

            var result = await _sql.EjecutarQueryAsync(database, sql);
            if (!result.Exitoso)
                return StatusCode(500, new { error = result.Error });

            var valores = result.Datos?
                .Select(row => row.TryGetValue(displayCol.NombreCampo, out var v) ? v?.ToString() : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList() ?? new List<string>();

            return Ok(new
            {
                tipo,
                valores,
                tabla = displayCol.NombreTabla,
                columna = displayCol.NombreCampo
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
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
