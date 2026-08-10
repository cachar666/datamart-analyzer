using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using DatamartAnalyzer.Api.Models;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Services;

/// <summary>
/// Almacén de consultas y dashboards. Vive DENTRO de cada base de datamart,
/// bajo el esquema ADP_DTM_APP — no hay una base central.
///
/// Por eso toda operación recibe el nombre del datamart: la conexión se arma
/// sobre la marcha a partir de DefaultConnection cambiando el catálogo inicial,
/// igual que hace SqlServerService.
/// </summary>
public interface IStoreRepository
{
    Task<bool> HayAlmacenAsync(string database);
    Task<StoreStatusDto> EstadoAsync(string database);

    // Consultas guardadas
    Task<List<SavedQueryDto>> ListarConsultasAsync(string database, string? buscar);
    Task<SavedQueryDto?> ObtenerConsultaAsync(string database, Guid id);
    Task<SavedQueryDto> CrearConsultaAsync(string database, CreateSavedQueryRequest req);
    Task<SavedQueryDto?> ActualizarConsultaAsync(string database, Guid id, UpdateSavedQueryRequest req);
    Task<bool> BorrarConsultaAsync(string database, Guid id);

    // Dashboards
    Task<List<DashboardDto>> ListarDashboardsAsync(string database);
    Task<DashboardDetailDto?> ObtenerDashboardAsync(string database, Guid id);
    Task<DashboardDto> CrearDashboardAsync(string database, CreateDashboardRequest req);
    Task<DashboardDto?> ActualizarDashboardAsync(string database, Guid id, UpdateDashboardRequest req);
    Task<bool> BorrarDashboardAsync(string database, Guid id);

    // Paneles
    Task<PanelDto?> AgregarPanelAsync(string database, Guid dashboardId, AddPanelRequest req);
    Task<PanelDto?> ActualizarPanelAsync(string database, Guid dashboardId, Guid panelId, UpdatePanelRequest req);
    Task<bool> QuitarPanelAsync(string database, Guid dashboardId, Guid panelId);
    Task<bool> ReordenarPanelesAsync(string database, Guid dashboardId, List<Guid> panelIds);

    // Presets de filtros
    Task<List<FilterPresetDto>> ListarPresetsAsync(string database);
    Task<FilterPresetDto> CrearPresetAsync(string database, CreateFilterPresetRequest req);
    Task<bool> BorrarPresetAsync(string database, Guid id);

    // Importación desde localStorage
    Task<ImportResultDto> ImportarAsync(string database, ImportRequest req);
}

public class StoreRepository : IStoreRepository
{
    /// <summary>Esquema donde vive el almacén, hermano de ADP_DTM_FACT / DIM / VCONF.</summary>
    public const string Schema = "ADP_DTM_APP";

    private readonly IDatamartConnectionResolver _conexiones;
    private readonly ILogger<StoreRepository> _logger;

    // JsonStringEnumConverter es obligatorio: ConfiguracionGrafico.Tipo es el enum
    // TipoGrafico. Sin el converter se persistiría como número (0, 1, 2...), ilegible
    // en la tabla y frágil si otro proceso escribiera el nombre del enum.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public StoreRepository(IDatamartConnectionResolver conexiones, ILogger<StoreRepository> logger)
    {
        _conexiones = conexiones;
        _logger = logger;
    }

    /// <summary>
    /// La conexión la decide IDatamartConnectionResolver: en modo ERP el datamart puede
    /// vivir en otro servidor y con otras credenciales, así que no se arma desde
    /// DefaultConnection.
    /// </summary>
    private async Task<SqlConnection> ConnAsync(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("Se requiere el nombre de la base de datos.", nameof(database));

        return new SqlConnection(await _conexiones.ResolverAsync(database));
    }

    // ── Filas crudas (mapeo Dapper) ──────────────────────────────────────────

    private sealed class SavedQueryRow
    {
        public Guid Id { get; set; }
        public string Pregunta { get; set; } = "";
        public string Sql { get; set; } = "";
        public string TipoRespuesta { get; set; } = "Tabla";
        public string? GraficoJson { get; set; }
        public string? Tags { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    private sealed class DashboardRow
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Descripcion { get; set; }
        public int TotalPaneles { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    private sealed class PanelRow
    {
        public Guid Id { get; set; }
        public Guid DashboardId { get; set; }
        public string Titulo { get; set; } = "";
        public string Sql { get; set; } = "";
        public string SqlBase { get; set; } = "";
        public bool EsPrebuilt { get; set; }
        public string? PrebuiltKey { get; set; }
        public string? FiltrosFijosJson { get; set; }
        public bool AceptaFiltrosGlobales { get; set; }
        public string TipoRespuesta { get; set; } = "Tabla";
        public string? GraficoJson { get; set; }
        public int Orden { get; set; }
    }

    private sealed class PresetRow
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string FiltrosJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    // ── Helpers de (de)serialización ─────────────────────────────────────────

    private static TipoRespuesta ParseTipo(string valor) =>
        Enum.TryParse<TipoRespuesta>(valor, ignoreCase: true, out var t) ? t : TipoRespuesta.Tabla;

    private static ConfiguracionGrafico? LeerGrafico(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ConfiguracionGrafico>(json, JsonOpts); }
        catch { return null; }
    }

    private static Dictionary<string, List<string>>? LeerFiltros(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOpts); }
        catch { return null; }
    }

    private static string? EscribirJson(object? valor) =>
        valor is null ? null : JsonSerializer.Serialize(valor, JsonOpts);

    private static SavedQueryDto Map(string database, SavedQueryRow r) => new(
        r.Id, database, r.Pregunta, r.Sql, ParseTipo(r.TipoRespuesta),
        LeerGrafico(r.GraficoJson), r.Tags, r.CreatedAt, r.UpdatedAt, r.CreatedBy);

    private static DashboardDto Map(string database, DashboardRow r) => new(
        r.Id, r.Nombre, database, r.Descripcion, r.TotalPaneles,
        r.CreatedAt, r.UpdatedAt, r.CreatedBy);

    private static PanelDto Map(PanelRow r) => new(
        r.Id, r.DashboardId, r.Titulo, r.Sql, r.SqlBase, r.EsPrebuilt, r.PrebuiltKey,
        LeerFiltros(r.FiltrosFijosJson), r.AceptaFiltrosGlobales,
        ParseTipo(r.TipoRespuesta), LeerGrafico(r.GraficoJson), r.Orden);

    private static FilterPresetDto Map(string database, PresetRow r) => new(
        r.Id, r.Nombre, database,
        LeerFiltros(r.FiltrosJson) ?? new Dictionary<string, List<string>>(),
        r.CreatedAt, r.CreatedBy);

    // ── Estado del almacén ───────────────────────────────────────────────────

    public async Task<bool> HayAlmacenAsync(string database)
    {
        try
        {
            await using var conn = await ConnAsync(database);
            var tablas = await conn.ExecuteScalarAsync<int>($"""
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = '{Schema}'
                  AND TABLE_NAME IN ('SavedQueries','Dashboards','DashboardPanels','FilterPresets')
                """);
            return tablas == 4;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo verificar el almacén en {database}", database);
            return false;
        }
    }

    public async Task<StoreStatusDto> EstadoAsync(string database)
    {
        if (!await HayAlmacenAsync(database))
            return new StoreStatusDto(database, false, 0, 0, 0,
                $"El almacén no está instalado en {database}. " +
                $"Ejecutá: dotnet run --project backend/DatamartAnalyzer.Migrations -- --database {database}");

        await using var conn = await ConnAsync(database);
        var consultas = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{Schema}].SavedQueries");
        var dashboards = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{Schema}].Dashboards");
        var paneles = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{Schema}].DashboardPanels");

        return new StoreStatusDto(database, true, consultas, dashboards, paneles, null);
    }

    // ── Consultas guardadas ──────────────────────────────────────────────────

    private static string SelectConsulta => $"""
        SELECT Id, Pregunta, Sql, TipoRespuesta, GraficoJson, Tags, CreatedAt, UpdatedAt, CreatedBy
        FROM [{Schema}].SavedQueries
        """;

    public async Task<List<SavedQueryDto>> ListarConsultasAsync(string database, string? buscar)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.QueryAsync<SavedQueryRow>($"""
            {SelectConsulta}
            WHERE (@buscar IS NULL OR Pregunta LIKE @patron OR ISNULL(Tags, '') LIKE @patron)
            ORDER BY CreatedAt DESC
            """,
            new
            {
                buscar,
                patron = string.IsNullOrWhiteSpace(buscar) ? null : $"%{Escapar(buscar)}%"
            });

        return filas.Select(f => Map(database, f)).ToList();
    }

    public async Task<SavedQueryDto?> ObtenerConsultaAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);
        var fila = await conn.QuerySingleOrDefaultAsync<SavedQueryRow>(
            $"{SelectConsulta} WHERE Id = @id", new { id });
        return fila is null ? null : Map(database, fila);
    }

    public async Task<SavedQueryDto> CrearConsultaAsync(string database, CreateSavedQueryRequest req)
    {
        await using var conn = await ConnAsync(database);
        var id = await conn.QuerySingleAsync<Guid>($"""
            INSERT INTO [{Schema}].SavedQueries (Pregunta, Sql, TipoRespuesta, GraficoJson, Tags)
            OUTPUT INSERTED.Id
            VALUES (@Pregunta, @Sql, @TipoRespuesta, @GraficoJson, @Tags)
            """,
            new
            {
                req.Pregunta,
                req.Sql,
                TipoRespuesta = req.TipoRespuesta.ToString(),
                GraficoJson = EscribirJson(req.Grafico),
                req.Tags
            });

        return (await ObtenerConsultaAsync(database, id))!;
    }

    public async Task<SavedQueryDto?> ActualizarConsultaAsync(string database, Guid id, UpdateSavedQueryRequest req)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.ExecuteAsync($"""
            UPDATE [{Schema}].SavedQueries
               SET Pregunta      = ISNULL(@Pregunta, Pregunta),
                   Sql           = ISNULL(@Sql, Sql),
                   TipoRespuesta = ISNULL(@TipoRespuesta, TipoRespuesta),
                   GraficoJson   = ISNULL(@GraficoJson, GraficoJson),
                   Tags          = ISNULL(@Tags, Tags),
                   UpdatedAt     = SYSUTCDATETIME()
             WHERE Id = @id
            """,
            new
            {
                id,
                req.Pregunta,
                req.Sql,
                TipoRespuesta = req.TipoRespuesta?.ToString(),
                GraficoJson = EscribirJson(req.Grafico),
                req.Tags
            });

        return filas == 0 ? null : await ObtenerConsultaAsync(database, id);
    }

    public async Task<bool> BorrarConsultaAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.ExecuteAsync($"DELETE FROM [{Schema}].SavedQueries WHERE Id = @id", new { id });
        return filas > 0;
    }

    // ── Dashboards ───────────────────────────────────────────────────────────

    public async Task<List<DashboardDto>> ListarDashboardsAsync(string database)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.QueryAsync<DashboardRow>($"""
            SELECT d.Id, d.Nombre, d.Descripcion,
                   (SELECT COUNT(*) FROM [{Schema}].DashboardPanels p WHERE p.DashboardId = d.Id) AS TotalPaneles,
                   d.CreatedAt, d.UpdatedAt, d.CreatedBy
            FROM [{Schema}].Dashboards d
            ORDER BY d.Nombre
            """);

        return filas.Select(f => Map(database, f)).ToList();
    }

    public async Task<DashboardDetailDto?> ObtenerDashboardAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);
        var dash = await conn.QuerySingleOrDefaultAsync<DashboardRow>($"""
            SELECT Id, Nombre, Descripcion, 0 AS TotalPaneles, CreatedAt, UpdatedAt, CreatedBy
            FROM [{Schema}].Dashboards WHERE Id = @id
            """, new { id });

        if (dash is null) return null;

        var paneles = await conn.QueryAsync<PanelRow>($"""
            SELECT Id, DashboardId, Titulo, Sql, SqlBase, EsPrebuilt, PrebuiltKey,
                   FiltrosFijosJson, AceptaFiltrosGlobales, TipoRespuesta, GraficoJson, Orden
            FROM [{Schema}].DashboardPanels
            WHERE DashboardId = @id
            ORDER BY Orden, Titulo
            """, new { id });

        return new DashboardDetailDto(
            dash.Id, dash.Nombre, database, dash.Descripcion,
            paneles.Select(Map).ToList(),
            dash.CreatedAt, dash.UpdatedAt, dash.CreatedBy);
    }

    public async Task<DashboardDto> CrearDashboardAsync(string database, CreateDashboardRequest req)
    {
        await using var conn = await ConnAsync(database);
        var id = await conn.QuerySingleAsync<Guid>($"""
            INSERT INTO [{Schema}].Dashboards (Nombre, Descripcion)
            OUTPUT INSERTED.Id
            VALUES (@Nombre, @Descripcion)
            """, new { req.Nombre, req.Descripcion });

        return (await ListarDashboardsAsync(database)).First(d => d.Id == id);
    }

    public async Task<DashboardDto?> ActualizarDashboardAsync(string database, Guid id, UpdateDashboardRequest req)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.ExecuteAsync($"""
            UPDATE [{Schema}].Dashboards
               SET Nombre      = ISNULL(@Nombre, Nombre),
                   Descripcion = ISNULL(@Descripcion, Descripcion),
                   UpdatedAt   = SYSUTCDATETIME()
             WHERE Id = @id
            """, new { id, req.Nombre, req.Descripcion });

        if (filas == 0) return null;

        var detalle = await ObtenerDashboardAsync(database, id);
        return detalle is null
            ? null
            : new DashboardDto(detalle.Id, detalle.Nombre, database, detalle.Descripcion,
                               detalle.Paneles.Count, detalle.CreatedAt, detalle.UpdatedAt, detalle.CreatedBy);
    }

    public async Task<bool> BorrarDashboardAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);
        // Los paneles caen por ON DELETE CASCADE.
        var filas = await conn.ExecuteAsync($"DELETE FROM [{Schema}].Dashboards WHERE Id = @id", new { id });
        return filas > 0;
    }

    // ── Paneles ──────────────────────────────────────────────────────────────

    public async Task<PanelDto?> AgregarPanelAsync(string database, Guid dashboardId, AddPanelRequest req)
    {
        string sql;
        var tipo = req.TipoRespuesta;
        var grafico = req.Grafico;

        if (req.SavedQueryId is Guid savedId)
        {
            var consulta = await ObtenerConsultaAsync(database, savedId);
            if (consulta is null) return null;
            sql = consulta.Sql;
            tipo = consulta.TipoRespuesta;
            grafico ??= consulta.Grafico;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.Sql)) return null;
            sql = req.Sql;
        }

        await using var conn = await ConnAsync(database);

        var siguienteOrden = await conn.ExecuteScalarAsync<int>($"""
            SELECT ISNULL(MAX(Orden), -1) + 1 FROM [{Schema}].DashboardPanels WHERE DashboardId = @dashboardId
            """, new { dashboardId });

        var id = await conn.QuerySingleOrDefaultAsync<Guid?>($"""
            INSERT INTO [{Schema}].DashboardPanels
                (DashboardId, Titulo, Sql, SqlBase, EsPrebuilt, PrebuiltKey,
                 FiltrosFijosJson, AceptaFiltrosGlobales, TipoRespuesta, GraficoJson, Orden)
            OUTPUT INSERTED.Id
            SELECT @dashboardId, @Titulo, @Sql, @Sql, @EsPrebuilt, @PrebuiltKey,
                   @FiltrosFijosJson, @AceptaFiltrosGlobales, @TipoRespuesta, @GraficoJson, @Orden
            WHERE EXISTS (SELECT 1 FROM [{Schema}].Dashboards WHERE Id = @dashboardId)
            """,
            new
            {
                dashboardId,
                req.Titulo,
                Sql = sql,
                req.EsPrebuilt,
                req.PrebuiltKey,
                FiltrosFijosJson = EscribirJson(req.FiltrosFijos),
                req.AceptaFiltrosGlobales,
                TipoRespuesta = tipo.ToString(),
                GraficoJson = EscribirJson(grafico),
                Orden = siguienteOrden
            });

        if (id is null) return null;   // el dashboard no existe

        await TocarDashboardAsync(conn, dashboardId);

        var detalle = await ObtenerDashboardAsync(database, dashboardId);
        return detalle?.Paneles.FirstOrDefault(p => p.Id == id.Value);
    }

    public async Task<PanelDto?> ActualizarPanelAsync(
        string database, Guid dashboardId, Guid panelId, UpdatePanelRequest req)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.ExecuteAsync($"""
            UPDATE [{Schema}].DashboardPanels
               SET Titulo                = ISNULL(@Titulo, Titulo),
                   AceptaFiltrosGlobales = ISNULL(@AceptaFiltrosGlobales, AceptaFiltrosGlobales),
                   FiltrosFijosJson      = ISNULL(@FiltrosFijosJson, FiltrosFijosJson)
             WHERE Id = @panelId AND DashboardId = @dashboardId
            """,
            new
            {
                dashboardId,
                panelId,
                req.Titulo,
                req.AceptaFiltrosGlobales,
                FiltrosFijosJson = EscribirJson(req.FiltrosFijos)
            });

        if (filas == 0) return null;

        await TocarDashboardAsync(conn, dashboardId);

        var detalle = await ObtenerDashboardAsync(database, dashboardId);
        return detalle?.Paneles.FirstOrDefault(p => p.Id == panelId);
    }

    public async Task<bool> QuitarPanelAsync(string database, Guid dashboardId, Guid panelId)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.ExecuteAsync($"""
            DELETE FROM [{Schema}].DashboardPanels WHERE Id = @panelId AND DashboardId = @dashboardId
            """, new { dashboardId, panelId });

        if (filas == 0) return false;

        // Cierra el hueco que dejó el panel borrado.
        await conn.ExecuteAsync($"""
            WITH Numerados AS (
                SELECT Id, ROW_NUMBER() OVER (ORDER BY Orden, Titulo) - 1 AS NuevoOrden
                FROM [{Schema}].DashboardPanels WHERE DashboardId = @dashboardId
            )
            UPDATE p SET p.Orden = n.NuevoOrden
            FROM [{Schema}].DashboardPanels p
            INNER JOIN Numerados n ON n.Id = p.Id
            """, new { dashboardId });

        await TocarDashboardAsync(conn, dashboardId);
        return true;
    }

    public async Task<bool> ReordenarPanelesAsync(string database, Guid dashboardId, List<Guid> panelIds)
    {
        if (panelIds.Count == 0) return false;

        await using var conn = await ConnAsync(database);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var existentes = (await conn.QueryAsync<Guid>(
                $"SELECT Id FROM [{Schema}].DashboardPanels WHERE DashboardId = @dashboardId",
                new { dashboardId }, tx)).ToHashSet();

            // Exige la lista completa: evita dejar órdenes duplicados o huecos.
            if (existentes.Count != panelIds.Count || !panelIds.All(existentes.Contains))
            {
                await tx.RollbackAsync();
                return false;
            }

            for (var i = 0; i < panelIds.Count; i++)
            {
                await conn.ExecuteAsync(
                    $"UPDATE [{Schema}].DashboardPanels SET Orden = @orden WHERE Id = @id AND DashboardId = @dashboardId",
                    new { orden = i, id = panelIds[i], dashboardId }, tx);
            }

            await conn.ExecuteAsync(
                $"UPDATE [{Schema}].Dashboards SET UpdatedAt = SYSUTCDATETIME() WHERE Id = @dashboardId",
                new { dashboardId }, tx);

            await tx.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Error reordenando paneles del dashboard {dashboardId} en {database}",
                dashboardId, database);
            throw;
        }
    }

    private static Task TocarDashboardAsync(SqlConnection conn, Guid dashboardId) =>
        conn.ExecuteAsync($"UPDATE [{Schema}].Dashboards SET UpdatedAt = SYSUTCDATETIME() WHERE Id = @dashboardId",
            new { dashboardId });

    // ── Presets de filtros ───────────────────────────────────────────────────

    public async Task<List<FilterPresetDto>> ListarPresetsAsync(string database)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.QueryAsync<PresetRow>($"""
            SELECT Id, Nombre, FiltrosJson, CreatedAt, CreatedBy
            FROM [{Schema}].FilterPresets
            ORDER BY Nombre
            """);

        return filas.Select(f => Map(database, f)).ToList();
    }

    public async Task<FilterPresetDto> CrearPresetAsync(string database, CreateFilterPresetRequest req)
    {
        await using var conn = await ConnAsync(database);
        var id = await conn.QuerySingleAsync<Guid>($"""
            INSERT INTO [{Schema}].FilterPresets (Nombre, FiltrosJson)
            OUTPUT INSERTED.Id
            VALUES (@Nombre, @FiltrosJson)
            """,
            new { req.Nombre, FiltrosJson = JsonSerializer.Serialize(req.Filtros, JsonOpts) });

        return (await ListarPresetsAsync(database)).First(p => p.Id == id);
    }

    public async Task<bool> BorrarPresetAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);
        var filas = await conn.ExecuteAsync($"DELETE FROM [{Schema}].FilterPresets WHERE Id = @id", new { id });
        return filas > 0;
    }

    // ── Importación desde localStorage ───────────────────────────────────────

    /// <summary>
    /// Importa lo que venía del navegador. Nunca pisa lo existente:
    /// las consultas se deduplican por SQL y los dashboards por nombre.
    /// Es seguro correrlo más de una vez.
    /// </summary>
    public async Task<ImportResultDto> ImportarAsync(string database, ImportRequest req)
    {
        var advertencias = new List<string>();
        int consultasOk = 0, consultasSkip = 0, dashOk = 0, dashSkip = 0, panelesOk = 0;

        await using var conn = await ConnAsync(database);
        await conn.OpenAsync();

        // ── Consultas ──
        foreach (var c in req.Consultas ?? [])
        {
            if (string.IsNullOrWhiteSpace(c.Sql))
            {
                advertencias.Add($"Consulta '{Recortar(c.Pregunta)}' omitida: sin SQL.");
                consultasSkip++;
                continue;
            }

            var yaExiste = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM [{Schema}].SavedQueries WHERE Sql = @sql", new { sql = c.Sql });

            if (yaExiste > 0) { consultasSkip++; continue; }

            await conn.ExecuteAsync($"""
                INSERT INTO [{Schema}].SavedQueries (Pregunta, Sql, TipoRespuesta, GraficoJson)
                VALUES (@Pregunta, @Sql, @TipoRespuesta, @GraficoJson)
                """,
                new
                {
                    Pregunta = Recortar(c.Pregunta, 1000),
                    c.Sql,
                    TipoRespuesta = NormalizarTipo(c.TipoRespuesta),
                    GraficoJson = EscribirJson(c.Grafico)
                });
            consultasOk++;
        }

        // ── Dashboards ──
        foreach (var d in req.Dashboards ?? [])
        {
            var existente = await conn.QuerySingleOrDefaultAsync<Guid?>(
                $"SELECT Id FROM [{Schema}].Dashboards WHERE Nombre = @nombre", new { nombre = d.Nombre });

            if (existente is not null)
            {
                advertencias.Add($"Dashboard '{d.Nombre}' ya existía: no se tocó.");
                dashSkip++;
                continue;
            }

            var dashId = await conn.QuerySingleAsync<Guid>($"""
                INSERT INTO [{Schema}].Dashboards (Nombre, Descripcion)
                OUTPUT INSERTED.Id
                VALUES (@Nombre, @Descripcion)
                """,
                new
                {
                    Nombre = Recortar(d.Nombre, 200),
                    Descripcion = "Importado desde el navegador"
                });
            dashOk++;

            var orden = 0;
            foreach (var p in d.Paneles ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.Sql))
                {
                    advertencias.Add($"Panel '{Recortar(p.Titulo)}' de '{d.Nombre}' omitido: sin SQL.");
                    continue;
                }

                await conn.ExecuteAsync($"""
                    INSERT INTO [{Schema}].DashboardPanels
                        (DashboardId, Titulo, Sql, SqlBase, EsPrebuilt, PrebuiltKey,
                         FiltrosFijosJson, AceptaFiltrosGlobales, TipoRespuesta, GraficoJson, Orden)
                    VALUES
                        (@DashboardId, @Titulo, @Sql, @SqlBase, @EsPrebuilt, @PrebuiltKey,
                         @FiltrosFijosJson, @AceptaFiltrosGlobales, @TipoRespuesta, @GraficoJson, @Orden)
                    """,
                    new
                    {
                        DashboardId = dashId,
                        Titulo = Recortar(p.Titulo, 200),
                        p.Sql,
                        SqlBase = p.SqlBase ?? p.Sql,
                        p.EsPrebuilt,
                        PrebuiltKey = RecortarOpcional(p.PrebuiltKey, 400),
                        FiltrosFijosJson = EscribirJson(p.FiltrosFijos),
                        p.AceptaFiltrosGlobales,
                        TipoRespuesta = NormalizarTipo(p.TipoRespuesta),
                        GraficoJson = EscribirJson(p.Grafico),
                        Orden = orden++
                    });
                panelesOk++;
            }
        }

        _logger.LogInformation(
            "Importación a {database}: {c} consultas, {d} dashboards, {p} paneles",
            database, consultasOk, dashOk, panelesOk);

        return new ImportResultDto(consultasOk, consultasSkip, dashOk, dashSkip, panelesOk, advertencias);
    }

    // ── Utilidades ───────────────────────────────────────────────────────────

    private static string NormalizarTipo(string? valor) => ParseTipo(valor ?? "Tabla").ToString();

    private static string Recortar(string? texto, int max = 80)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Length <= max ? texto : texto[..max];
    }

    /// <summary>
    /// Igual que Recortar pero preserva el null. Para columnas nullable como PrebuiltKey,
    /// donde guardar "" en lugar de NULL rompería la distinción entre "sin prebuilt" y
    /// "prebuilt con clave vacía".
    /// </summary>
    private static string? RecortarOpcional(string? texto, int max)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        return texto.Length <= max ? texto : texto[..max];
    }

    /// <summary>Escapa los comodines de LIKE para que una búsqueda con % o _ no se descontrole.</summary>
    private static string Escapar(string texto) =>
        texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
