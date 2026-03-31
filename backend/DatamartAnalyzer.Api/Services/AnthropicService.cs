using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DatamartAnalyzer.Api.Models;

namespace DatamartAnalyzer.Api.Services;

public interface IAnthropicService
{
    Task<(AiRawResponse Response, UsageInfo? Usage)> AnalizarPreguntaAsync(AnalyzeRequest request);
    Task<(AiRawResponse Response, UsageInfo? Usage)> CorregirSqlAsync(AnalyzeRequest request, string sqlFallido, string errorSql);
}

public class AnthropicService : IAnthropicService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly int _maxCharsDocumento;   // máx chars del bloque RAG en el system prompt
    private readonly int _maxCharsQuery;       // máx chars de secciones relevantes por query
    private readonly ILogger<AnthropicService> _logger;
    private readonly IDocumentService _documentService;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicService(
        HttpClient http,
        IConfiguration config,
        ILogger<AnthropicService> logger,
        IDocumentService documentService)
    {
        _http = http;
        _apiKey = config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey no configurado.");
        _model = config["Anthropic:Model"] ?? "claude-sonnet-4-20250514";
        _maxTokens = config.GetValue("Anthropic:MaxTokens", 4096);
        _maxCharsDocumento = config.GetValue("Rag:MaxCharsDocumento", 6000);
        _maxCharsQuery = config.GetValue("Rag:MaxCharsQuery", 2500);
        _logger = logger;
        _documentService = documentService;

        _http.BaseAddress = new Uri("https://api.anthropic.com");
        _http.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");
    }

    public async Task<(AiRawResponse Response, UsageInfo? Usage)> AnalizarPreguntaAsync(AnalyzeRequest request)
    {
        var modelo = request.Modelo ?? _model;

        // Bloque 1 (cacheado entre TODAS las consultas): instrucciones + doc ERP completo
        // Bloque 2 (cacheado por BD): schema de la BD seleccionada
        // Mensaje usuario: pregunta + secciones ERP relevantes para la pregunta
        var payload = new
        {
            model = modelo,
            max_tokens = _maxTokens,
            system = new[]
            {
                new
                {
                    type = "text",
                    text = BuildParteEstatica(),
                    cache_control = new { type = "ephemeral" }
                },
                new
                {
                    type = "text",
                    text = BuildParteSchema(request),
                    cache_control = new { type = "ephemeral" }
                }
            },
            messages = new[]
            {
                new { role = "user", content = BuildMensajeUsuario(request) }
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Enviando pregunta a Claude: {pregunta}", request.Pregunta);

        var response = await _http.PostAsync("/v1/messages", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Error Anthropic API: {status} - {body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);

        UsageInfo? usageInfo = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
            usageInfo = ExtractUsage(usage, modelo);

        var textContent = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        var cleanedJson = LimpiarJson(textContent);

        try
        {
            var aiResponse = JsonSerializer.Deserialize<AiRawResponse>(cleanedJson, JsonOpts)
                ?? new AiRawResponse("Texto", "No se pudo interpretar la respuesta.", null, null);
            return (aiResponse, usageInfo);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parseando JSON de Claude: {json}", cleanedJson);
            return (new AiRawResponse("Texto", textContent, null, null), usageInfo);
        }
    }

    // ── Bloque 1: instrucciones fijas + documento ERP (cacheado entre todas las consultas) ──

    private string BuildParteEstatica()
    {
        var sb = new StringBuilder();

        sb.Append("""
            Eres un experto analista de datos del ERP ADPRO/SincoERP.
            Responde preguntas sobre el datamart usando el schema proporcionado.

            REGLA ABSOLUTA — COLUMNAS:
            PROHIBIDO usar cualquier columna que no esté EXPLÍCITAMENTE listada en el schema de esa tabla.
            Esto incluye columnas de filtro (WHERE), agrupación (GROUP BY), selección (SELECT) y JOIN (ON).
            Si una columna no aparece en el schema de la tabla, NO existe. No la uses bajo ningún concepto.

            REGLAS:
            1. Responde SOLO con JSON válido. Sin texto extra, sin markdown, sin explicaciones fuera del JSON.
            2. SOLO genera SELECT. Nunca INSERT, UPDATE, DELETE, DROP, ALTER, CREATE, TRUNCATE, EXEC.
            3. Usa nombres EXACTOS de tablas y columnas del schema. Copia el nombre literal, incluyendo espacios y mayúsculas.
            4. Consulta ÚNICAMENTE las tablas del schema. No inventes tablas ni columnas.
            5. Para JOINs: SOLO usa columnas marcadas con * que estén listadas en AMBAS tablas. Nunca inferas que una llave existe en una tabla porque aparece en otra.
            6. Si la pregunta requiere datos que no están en el schema disponible, explícalo en ExplicacionTexto y omite SqlGenerado.
            7. Antes de generar el SQL, verifica mentalmente que cada columna usada existe en el schema de su tabla.

            REGLA CRÍTICA — ControlClaseOrigen:
            Filtrar SIEMPRE por co.[Clase], nunca por co.[Origen] para la clasificación principal. [Origen] es el subtipo del documento dentro de cada Clase.
            [Clase]: 'P'=Presupuestado | 'Y'=Proyectado | 'L'=Actas Cliente | 'C'=Consumido | 'T'=Asegurado contratos | 'B'=Asegurado compras | 'J'=Ejecutado | 'I'=Invertido
            [Clase]+[Origen] para tipo de documento exacto:
              C+S=Salida Almacén | C+G=Actas Generales | C+R=Actas Por Grupo | C+T=Actas Todo Costo | C+D=Reintegro Salida | C+M=Descuentos Menor Valor | C+C=Cuentas Control | C+E=Equipos
              T+G=Contratos Generales | T+R=Contratos Por Grupos | T+T=Contratos Todo Costo | T+N=Nómina Contratado | T+C=Cuentas Control | T+D=Descuentos Menor Valor
              B+C=Órdenes de Compra | B+TE=Entradas Traslado | B+TS=Salidas Traslado | B+EX=Salidas Transformación | B+SX=Entradas Transformación
              J+E=Actas de Avance
              I+E=Entradas Almacén | I+NP=Entradas No Asignadas | I+N=Control Nómina | I+EQ=Equipos | I+G=Actas Generales | I+R=Actas Por Grupo | I+T=Actas Todo Costo | I+K=Cuentas Control | I+V=Notas Valor | I+VN=Notas Valor Sin Asignación | I+X=Devoluciones Proveedor | I+ED=Devoluciones Proveedor | I+TE=Entradas Traslado | I+TS=Salidas Traslado | I+EX=Entradas Transformación | I+SX=Salidas Transformación | I+AD=Descuentos Contables | I+SO=Saldo Anticipos OC | I+SC=Saldo Anticipos Contratos | I+AJ=Ajustes Inventario
            NUNCA usar co.[Origen]='P' — ese valor no existe en [Origen], solo en [Clase].
            INVENTARIO (solo movimientos físicos) = SUM(CASE WHEN co.[Clase]='I' AND co.[Origen] IN ('E','NP','ED','V','VN','X','TE','TS','EX','SX','AJ') THEN cp.[Valor Total] ELSE 0 END) - SUM(CASE WHEN co.[Clase]='C' AND co.[Origen] IN ('S','D') THEN cp.[Valor Total] ELSE 0 END). No existe Clase='Inventario'. No usar todos los orígenes de I ni de C — solo los listados.
            Valor monetario: [Valor Total], no [Valor Sin IVA].
            Estados válidos de proyecto (únicos 4): 'Presupuesto' | 'En ejecucion' | 'Inactivo' | 'Finalizado'. Proyectos activos/en curso → [Estado]='En ejecucion'. PROHIBIDO: 'Activo', 'Terminado', 'En planeacion'.
            JOINs SIEMPRE con SkIdEmpresa: ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa

            FORMATO JSON:
            {"TipoRespuesta":"Tabla|Grafico|Texto|TablaMasGrafico|TablaMasTexto","ExplicacionTexto":"(español)","SqlGenerado":"SELECT...","Grafico":{"Tipo":"Barras|Lineas|Torta|Area","CampoEjeX":"col","CampoEjeY":"col","CampoAgrupacion":null,"Titulo":"título","ColorPrimario":"#3b82f6"}}

            TipoRespuesta: Tabla=listados/detalles, Grafico=totales/rankings/tendencias, Texto=conteos/conceptual, TablaMasGrafico=detalle+visual, TablaMasTexto=tabla+interpretación

            """);

        if (_documentService.HayDocumento)
        {
            sb.AppendLine("CONTEXTO ERP ADPRO:");
            var doc = _documentService.ObtenerContextoErp();
            // Limita el tamaño del documento en el prompt cacheado
            sb.Append(doc.Length <= _maxCharsDocumento
                ? doc
                : doc[.._maxCharsDocumento] + "\n[...documento truncado...]");
        }

        return sb.ToString();
    }

    // ── Bloque 2: schema de la BD (cacheado por BD) ──────────────────────────────

    private static string BuildParteSchema(AnalyzeRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BD:[{request.Database}]");
        sb.AppendLine("Consulta SOLO estas tablas:");
        sb.AppendLine();

        var hechos = request.Schema
            .Where(c => c.TipoObjeto.Equals("HECHO", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.NombreTabla);

        var dimensiones = request.Schema
            .Where(c => c.TipoObjeto.Equals("DIMENSION", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.NombreTabla);

        // Formato compacto: una línea por tabla, campos separados por |
        // *Campo:tipo = llave surrogate  |  Campo:tipo=desc = campo con descripción
        sb.AppendLine("HECHOS:");
        foreach (var tabla in hechos)
        {
            sb.Append(tabla.Key);
            sb.Append(": ");
            sb.AppendLine(string.Join(" | ", tabla.Select(FormatCol)));
        }

        sb.AppendLine("DIMENSIONES:");
        foreach (var tabla in dimensiones)
        {
            sb.Append(tabla.Key);
            sb.Append(": ");
            sb.AppendLine(string.Join(" | ", tabla.Select(FormatCol)));
        }

        if (request.Vistas.Count > 0)
        {
            sb.AppendLine("VISTAS:");
            foreach (var v in request.Vistas.Take(50))
                sb.AppendLine($"  [{v.Esquema}].[{v.NombreVista}]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formato compacto de columna:
    ///   *SkId:int          → llave surrogate
    ///   Campo:decimal=Desc → con descripción corta
    ///   Campo:nvarchar     → sin descripción
    /// </summary>
    private static string FormatCol(SchemaColumn col)
    {
        var llave = col.EsLlave ? "*" : "";
        var tipo = AbrevirTipo(col.TipoCampo);
        var desc = col.DescripcionCampo is { Length: > 0 } d ? $"={TruncDesc(d)}" : "";
        return $"{llave}{col.NombreCampo}:{tipo}{desc}";
    }

    private static string AbrevirTipo(string tipo) => tipo.ToLowerInvariant() switch
    {
        "int" or "integer"         => "int",
        "bigint"                   => "bigint",
        "smallint"                 => "smallint",
        "decimal" or "numeric"     => "dec",
        "float" or "real"          => "float",
        "money" or "smallmoney"    => "money",
        "bit"                      => "bit",
        "date"                     => "date",
        "datetime" or "datetime2"  => "dt",
        "nvarchar" or "varchar"    => "str",
        "nchar" or "char"          => "str",
        "uniqueidentifier"         => "guid",
        _                          => tipo.Length > 6 ? tipo[..6] : tipo
    };

    private static string TruncDesc(string desc)
        => desc.Length <= 40 ? desc : desc[..37] + "…";

    // ── Mensaje de usuario: filtros activos + secciones ERP relevantes + pregunta ─

    private string BuildMensajeUsuario(AnalyzeRequest request)
    {
        var sb = new StringBuilder();

        // Bloque 1: filtros de contexto activos (empresa, proyecto, macroproyecto seleccionados)
        if (request.ContextoVariables is { Count: > 0 })
        {
            var activos = request.ContextoVariables
                .Where(kv => kv.Value?.Count > 0)
                .ToList();
            if (activos.Count > 0)
            {
                sb.AppendLine("[FILTROS DE CONTEXTO ACTIVOS]");
                sb.AppendLine("Aplica SIEMPRE estos filtros en el WHERE de tu SQL a menos que el usuario pida explícitamente otro valor:");
                foreach (var kv in activos)
                {
                    var vals = string.Join(", ", kv.Value.Select(v => $"'{v}'"));
                    if (request.FilterMeta != null && request.FilterMeta.TryGetValue(kv.Key, out var meta))
                        sb.AppendLine($"- {kv.Key} → {meta.Tabla}.[{meta.Columna}] IN ({vals})");
                    else
                        sb.AppendLine($"- {kv.Key}: IN ({vals})");
                }
                sb.AppendLine();
            }
        }

        // Bloque 2: secciones ERP relevantes (RAG)
        if (_documentService.HayDocumento)
        {
            var secciones = _documentService.ObtenerSeccionesRelevantes(request.Pregunta, _maxCharsQuery);
            if (!string.IsNullOrWhiteSpace(secciones))
            {
                sb.AppendLine("[CONTEXTO ERP RELEVANTE]");
                sb.AppendLine(secciones);
                sb.AppendLine();
            }
        }

        if (sb.Length == 0)
            return request.Pregunta;

        sb.AppendLine("[PREGUNTA]");
        sb.Append(request.Pregunta);
        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private UsageInfo ExtractUsage(JsonElement usage, string modelo)
    {
        var tokensEntrada    = usage.TryGetProperty("input_tokens",               out var v1) ? v1.GetInt32() : 0;
        var tokensCacheWrite = usage.TryGetProperty("cache_creation_input_tokens", out var v2) ? v2.GetInt32() : 0;
        var tokensCacheRead  = usage.TryGetProperty("cache_read_input_tokens",    out var v3) ? v3.GetInt32() : 0;
        var tokensSalida     = usage.TryGetProperty("output_tokens",              out var v4) ? v4.GetInt32() : 0;

        var (precioIn, precioWrite, precioRead, precioOut) = modelo.Contains("haiku")
            ? (0.80, 1.00, 0.08, 4.00)
            : modelo.Contains("opus")
                ? (15.0, 18.75, 1.50, 75.0)
                : (3.00, 3.75, 0.30, 15.0);

        var costoTotal = (tokensEntrada    / 1_000_000.0) * precioIn
                       + (tokensCacheWrite / 1_000_000.0) * precioWrite
                       + (tokensCacheRead  / 1_000_000.0) * precioRead
                       + (tokensSalida     / 1_000_000.0) * precioOut;

        _logger.LogInformation(
            "[{modelo}] Tokens — in:{in} cw:{cw} cr:{cr} out:{out} | ${costo:F5} USD",
            modelo, tokensEntrada, tokensCacheWrite, tokensCacheRead, tokensSalida, costoTotal);

        return new UsageInfo(tokensEntrada, tokensSalida, tokensCacheWrite, tokensCacheRead, costoTotal, modelo);
    }

    // ── Corrección de SQL fallido ─────────────────────────────────────────────

    public async Task<(AiRawResponse Response, UsageInfo? Usage)> CorregirSqlAsync(AnalyzeRequest request, string sqlFallido, string errorSql)
    {
        var modelo = request.Modelo ?? _model;

        var payload = new
        {
            model = modelo,
            max_tokens = _maxTokens,
            system = new[]
            {
                new
                {
                    type = "text",
                    text = BuildParteEstatica(),
                    cache_control = new { type = "ephemeral" }
                },
                new
                {
                    type = "text",
                    text = BuildParteSchema(request),
                    cache_control = new { type = "ephemeral" }
                }
            },
            messages = new[]
            {
                new { role = "user", content = BuildMensajeUsuario(request) },
                new
                {
                    role = "assistant",
                    content = $"{{\"TipoRespuesta\":\"Tabla\",\"ExplicacionTexto\":\"Generando...\",\"SqlGenerado\":{System.Text.Json.JsonSerializer.Serialize(sqlFallido)},\"Grafico\":null}}"
                },
                new
                {
                    role = "user",
                    content = $"""
                        El SQL anterior falló con este error de SQL Server:
                        {errorSql}

                        IMPORTANTE: Ese error indica que una o más columnas usadas NO existen en esas tablas.
                        Revisa el schema y genera un nuevo SQL usando ÚNICAMENTE las columnas listadas.
                        Si no es posible responder la pregunta con las columnas disponibles, usa TipoRespuesta "Texto" y explica qué datos faltan.
                        Responde SOLO con JSON válido.
                        """
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        _logger.LogInformation("Reintentando con corrección de SQL por error: {error}", errorSql[..Math.Min(200, errorSql.Length)]);

        var response = await _http.PostAsync("/v1/messages", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");

        using var doc = System.Text.Json.JsonDocument.Parse(responseBody);

        UsageInfo? usageInfo2 = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
            usageInfo2 = ExtractUsage(usage, modelo);

        var textContent = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        var cleanedJson = LimpiarJson(textContent);

        try
        {
            var aiResponse = System.Text.Json.JsonSerializer.Deserialize<AiRawResponse>(cleanedJson, JsonOpts)
                ?? new AiRawResponse("Texto", "No se pudo corregir el SQL.", null, null);
            return (aiResponse, usageInfo2);
        }
        catch
        {
            return (new AiRawResponse("Texto", textContent, null, null), usageInfo2);
        }
    }

    private static string LimpiarJson(string text)
    {
        var t = text.Trim();
        // Quitar bloques markdown
        if (t.StartsWith("```json")) t = t[7..];
        else if (t.StartsWith("```"))  t = t[3..];
        if (t.EndsWith("```")) t = t[..^3];
        t = t.Trim();
        // Extraer el objeto JSON aunque haya texto libre antes o después
        var start = t.IndexOf('{');
        var end   = t.LastIndexOf('}');
        if (start >= 0 && end > start)
            t = t[start..(end + 1)];
        return t;
    }
}
