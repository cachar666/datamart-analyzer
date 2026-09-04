using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using DatamartAnalyzer.Core.Charts;
using DatamartAnalyzer.Core.Models;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Core.Store;

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

    /// <summary>Una sola conexión responde las dos preguntas: ¿se llega? ¿está instalado?</summary>
    Task<(bool Alcanzable, bool Instalado, string? Error)> VerificarAlmacenAsync(string database);

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

    // Preguntas sugeridas
    Task<List<SuggestedQuestionDto>> ListarSugeridasAsync(
        string database, string? modulo = null, bool incluirOcultas = false);
    Task<SuggestedQuestionDto> CrearSugeridaAsync(string database, CreateSuggestedQuestionRequest req);
    Task<SuggestedQuestionDto?> ActualizarSugeridaAsync(string database, Guid id, UpdateSuggestedQuestionRequest req);
    Task<bool> BorrarSugeridaAsync(string database, Guid id);
    Task<bool> RegistrarUsoSugeridaAsync(string database, Guid id);

    // Categorías del catálogo
    Task<List<SuggestedCategoryDto>> ListarCategoriasAsync(string database, string? modulo = null);
    Task<SuggestedCategoryDto> CrearCategoriaAsync(string database, CreateSuggestedCategoryRequest req);
    Task<bool> BorrarCategoriaAsync(string database, Guid id);

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

    private readonly IConexionDatamart _conexiones;
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

    public StoreRepository(IConexionDatamart conexiones, ILogger<StoreRepository> logger)
    {
        _conexiones = conexiones;
        _logger = logger;
    }

    /// <summary>
    /// La conexión la decide IConexionDatamart: en modo ERP el datamart puede
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

    /// <summary>
    /// Lee la configuracion del grafico y devuelve null si NO se puede dibujar.
    ///
    /// GraficoJson es texto libre, y hay paneles guardados con otro esquema
    /// —{"tipo","ejeX","series":[...]}— que deserializa "bien" pero deja CampoEjeX y
    /// CampoEjeY vacios. Devolver ese objeto a medias hacia que el front dibujara
    /// unos ejes sin nada adentro.
    ///
    /// Graficos.Leer entiende las dos formas y devuelve null cuando de verdad no hay
    /// nada dibujable, asi los paneles que ya estan guardados con el esquema viejo
    /// vuelven a pintar sin que nadie tenga que rehacerlos.
    /// </summary>
    private static ConfiguracionGrafico? LeerGrafico(string? json) => Graficos.Leer(json);

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

    /// <summary>
    /// Distingue las dos formas de "no hay almacén", que exigen respuestas opuestas:
    /// si NO SE LLEGA al servidor no hay nada que instalar, y decirle a alguien que
    /// corra el script de instalación lo manda a perder el tiempo. Antes las dos
    /// caían en el mismo `false`.
    /// </summary>
    public async Task<(bool Alcanzable, bool Instalado, string? Error)> VerificarAlmacenAsync(string database)
    {
        try
        {
            await using var conn = await ConnAsync(database);
            var tablas = await conn.ExecuteScalarAsync<int>($"""
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = '{Schema}'
                  AND TABLE_NAME IN ('SavedQueries','Dashboards','DashboardPanels','FilterPresets')
                """);
            return (true, tablas == 4, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo verificar el almacén en {database}", database);
            return (false, false, ex.Message);
        }
    }

    public async Task<bool> HayAlmacenAsync(string database) =>
        (await VerificarAlmacenAsync(database)).Instalado;

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
        // Misma regla que en los paneles: no se guarda una consulta que promete un
        // grafico sin traerlo. Un favorito que dibuja en blanco es peor que uno que
        // muestra la tabla.
        var grafico = Graficos.Normalizar(req.Grafico);
        var tipo = grafico is null && req.TipoRespuesta is TipoRespuesta.Grafico or TipoRespuesta.TablaMasGrafico
            ? TipoRespuesta.Tabla
            : req.TipoRespuesta;

        // Una consulta guardada SI puede ser Texto legitimamente (un conteo, una
        // respuesta conceptual), asi que aca no se degrada. La diferencia con el panel
        // es que la burbuja del chat sabe pintar prosa y el panel del dashboard no.

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
                TipoRespuesta = tipo.ToString(),
                GraficoJson = EscribirJson(grafico),
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

        // Un panel no puede prometer un grafico que no tiene. Guardarlo asi era la
        // forma de terminar con el recuadro vacio del dashboard: TipoRespuesta decia
        // TablaMasGrafico, GraficoJson estaba en NULL, y nadie se enteraba hasta
        // abrirlo. Se corrige aca porque es el unico camino que comparten la API, el
        // conector y la importacion.
        grafico = Graficos.Normalizar(grafico);
        if (grafico is null && tipo is TipoRespuesta.Grafico or TipoRespuesta.TablaMasGrafico)
            tipo = TipoRespuesta.Tabla;

        // Texto tampoco sirve para un panel: el visor dibuja tabla o grafico, no prosa,
        // asi que un panel marcado Texto quedaba con el cuerpo en blanco de forma
        // permanente — sin error, sin "sin resultados", con las filas cargadas detras.
        // Y llega solo: si el mapeo del enum degrada una respuesta a Texto, el panel se
        // guarda con ese tipo. Un panel siempre tiene SQL, asi que Tabla es la unica
        // salida honesta.
        if (tipo is TipoRespuesta.Texto)
            tipo = TipoRespuesta.Tabla;

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

    // ── Preguntas sugeridas ──────────────────────────────────────────────────

    private sealed class SugeridaRow
    {
        public Guid Id { get; set; }
        public string? ClaveSistema { get; set; }
        public string Modulo { get; set; } = "";
        public string Categoria { get; set; } = "";
        public string Pregunta { get; set; } = "";
        public string Origen { get; set; } = "";
        public bool Activa { get; set; }
        public int Orden { get; set; }
        public int UsosTotales { get; set; }
        public int UsosPropios { get; set; }
        public DateTime? UltimoUso { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    /// <summary>
    /// El catálogo con sus contadores. Los usos se cuentan en el mismo SELECT, uno
    /// global y otro del usuario actual — decidido así porque el Top del equipo y el
    /// personal se muestran en la misma pantalla y separarlos serían dos viajes.
    ///
    /// Por defecto NO devuelve las ocultas: quien las ocultó no quiere verlas. La
    /// pantalla de administración pide incluirOcultas para poder reactivarlas.
    /// </summary>
    public async Task<List<SuggestedQuestionDto>> ListarSugeridasAsync(
        string database, string? modulo = null, bool incluirOcultas = false)
    {
        await using var conn = await ConnAsync(database);

        var filas = await conn.QueryAsync<SugeridaRow>($"""
            SELECT  q.Id, q.ClaveSistema, q.Modulo, q.Categoria, q.Pregunta,
                    q.Origen, q.Activa, q.Orden, q.CreatedAt, q.CreatedBy,
                    UsosTotales = (SELECT COUNT(*) FROM [{Schema}].SuggestedQuestionUsage u
                                    WHERE u.QuestionId = q.Id),
                    UsosPropios = (SELECT COUNT(*) FROM [{Schema}].SuggestedQuestionUsage u
                                    WHERE u.QuestionId = q.Id AND u.Usuario = SUSER_SNAME()),
                    UltimoUso   = (SELECT MAX(u.UsedAt) FROM [{Schema}].SuggestedQuestionUsage u
                                    WHERE u.QuestionId = q.Id)
            FROM [{Schema}].SuggestedQuestions q
            WHERE (@modulo IS NULL OR q.Modulo = @modulo)
              AND (@incluirOcultas = 1 OR q.Activa = 1)
            ORDER BY q.Modulo, q.Orden, q.Categoria
            """,
            new { modulo, incluirOcultas });

        return filas.Select(f => MapSugerida(database, f)).ToList();
    }

    private static SuggestedQuestionDto MapSugerida(string database, SugeridaRow f) =>
        new(f.Id, database, f.ClaveSistema, f.Modulo, f.Categoria, f.Pregunta,
            Enum.TryParse<OrigenPregunta>(f.Origen, ignoreCase: true, out var o) ? o : OrigenPregunta.Usuario,
            f.Activa, f.Orden, f.UsosTotales, f.UsosPropios, f.UltimoUso, f.CreatedAt, f.CreatedBy);

    /// <summary>
    /// Crea una pregunta del usuario. ClaveSistema queda en NULL a propósito: es lo
    /// que la distingue de las nuestras y lo que impide que una re-siembra la pise.
    /// </summary>
    public async Task<SuggestedQuestionDto> CrearSugeridaAsync(
        string database, CreateSuggestedQuestionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pregunta))
            throw new ArgumentException("La pregunta no puede estar vacía.", nameof(req));

        var modulo = string.IsNullOrWhiteSpace(req.Modulo) ? "ADPRO" : req.Modulo.Trim().ToUpperInvariant();
        if (modulo is not ("ADPRO" or "SRM"))
            throw new ArgumentException($"Módulo '{modulo}' no válido. Se espera ADPRO o SRM.", nameof(req));

        var categoria = string.IsNullOrWhiteSpace(req.Categoria) ? "Mis preguntas" : req.Categoria.Trim();

        await using var conn = await ConnAsync(database);

        // Si la categoría no existía, escribirla acá ES crearla. Obligar a crearla
        // aparte primero sería un paso de más sin ninguna ganancia.
        await AsegurarCategoriaAsync(conn, modulo, categoria);

        // Va al final de su módulo, no al principio: agregar una propia no debería
        // reordenar lo que el usuario ya tenía a la vista.
        var id = await conn.QuerySingleAsync<Guid>($"""
            INSERT INTO [{Schema}].SuggestedQuestions (Modulo, Categoria, Pregunta, Origen, Orden)
            OUTPUT INSERTED.Id
            SELECT @modulo, @categoria, @pregunta, 'Usuario',
                   ISNULL((SELECT MAX(Orden) FROM [{Schema}].SuggestedQuestions WHERE Modulo = @modulo), 0) + 1
            """,
            new { modulo, categoria, pregunta = req.Pregunta.Trim() });

        return (await ListarSugeridasAsync(database, modulo, incluirOcultas: true)).First(q => q.Id == id);
    }

    /// <summary>
    /// Actualiza una pregunta. De las del sistema sólo se puede tocar Activa y Orden:
    /// el texto y la categoría los manda el catálogo, y dejar que se editen crearía
    /// una versión local que la próxima re-siembra pisaría sin avisar.
    /// </summary>
    public async Task<SuggestedQuestionDto?> ActualizarSugeridaAsync(
        string database, Guid id, UpdateSuggestedQuestionRequest req)
    {
        await using var conn = await ConnAsync(database);

        var origen = await conn.QuerySingleOrDefaultAsync<string>(
            $"SELECT Origen FROM [{Schema}].SuggestedQuestions WHERE Id = @id", new { id });

        if (origen is null) return null;

        var esSistema = origen.Equals("Sistema", StringComparison.OrdinalIgnoreCase);

        if (esSistema && (req.Pregunta is not null || req.Categoria is not null))
            throw new InvalidOperationException(
                "No se puede cambiar el texto ni la categoría de una pregunta del sistema; " +
                "la próxima actualización lo revertiría. Ocultala y creá la tuya.");

        await conn.ExecuteAsync($"""
            UPDATE [{Schema}].SuggestedQuestions
               SET Pregunta  = COALESCE(@pregunta, Pregunta),
                   Categoria = COALESCE(@categoria, Categoria),
                   Activa    = COALESCE(@activa, Activa),
                   Orden     = COALESCE(@orden, Orden),
                   UpdatedAt = SYSUTCDATETIME()
             WHERE Id = @id
            """,
            new { id, pregunta = req.Pregunta, categoria = req.Categoria, activa = req.Activa, orden = req.Orden });

        return (await ListarSugeridasAsync(database, null, incluirOcultas: true)).FirstOrDefault(q => q.Id == id);
    }

    /// <summary>
    /// Borra una pregunta del usuario. Las del sistema NO se borran — se ocultan con
    /// Activa = 0. Si se borraran, la próxima re-siembra las traería de vuelta y
    /// parecería un error del producto.
    /// </summary>
    public async Task<bool> BorrarSugeridaAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);

        var origen = await conn.QuerySingleOrDefaultAsync<string>(
            $"SELECT Origen FROM [{Schema}].SuggestedQuestions WHERE Id = @id", new { id });

        if (origen is null) return false;

        if (origen.Equals("Sistema", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Las preguntas del sistema no se borran: se ocultan. " +
                "La próxima actualización del catálogo volvería a traerla.");

        var filas = await conn.ExecuteAsync(
            $"DELETE FROM [{Schema}].SuggestedQuestions WHERE Id = @id", new { id });

        return filas > 0;
    }

    /// <summary>
    /// Deja constancia de que alguien lanzó esta pregunta. Se guarda el evento y no
    /// un contador porque hacen falta las dos vistas: el Top del equipo y el de cada
    /// uno.
    /// </summary>
    public async Task<bool> RegistrarUsoSugeridaAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);

        var filas = await conn.ExecuteAsync($"""
            INSERT INTO [{Schema}].SuggestedQuestionUsage (QuestionId)
            SELECT Id FROM [{Schema}].SuggestedQuestions WHERE Id = @id
            """, new { id });

        return filas > 0;
    }

    // ── Categorías del catálogo ──────────────────────────────────────────────

    private sealed class CategoriaRow
    {
        public Guid Id { get; set; }
        public string Modulo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Origen { get; set; } = "";
        public int Orden { get; set; }
        public int Preguntas { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    public async Task<List<SuggestedCategoryDto>> ListarCategoriasAsync(string database, string? modulo = null)
    {
        await using var conn = await ConnAsync(database);

        var filas = await conn.QueryAsync<CategoriaRow>($"""
            SELECT c.Id, c.Modulo, c.Nombre, c.Origen, c.Orden, c.CreatedAt, c.CreatedBy,
                   Preguntas = (SELECT COUNT(*) FROM [{Schema}].SuggestedQuestions q
                                 WHERE q.Modulo = c.Modulo AND q.Categoria = c.Nombre)
            FROM [{Schema}].SuggestedCategories c
            WHERE (@modulo IS NULL OR c.Modulo = @modulo)
            ORDER BY c.Modulo, c.Orden, c.Nombre
            """, new { modulo });

        return filas.Select(f => new SuggestedCategoryDto(
            f.Id, database, f.Modulo, f.Nombre,
            Enum.TryParse<OrigenPregunta>(f.Origen, true, out var o) ? o : OrigenPregunta.Usuario,
            f.Orden, f.Preguntas, f.CreatedAt, f.CreatedBy)).ToList();
    }

    public async Task<SuggestedCategoryDto> CrearCategoriaAsync(
        string database, CreateSuggestedCategoryRequest req)
    {
        var (modulo, nombre) = ValidarCategoria(req.Modulo, req.Nombre);

        await using var conn = await ConnAsync(database);

        var yaEsta = await conn.QuerySingleOrDefaultAsync<string>($"""
            SELECT Origen FROM [{Schema}].SuggestedCategories
            WHERE Modulo = @modulo AND Nombre = @nombre
            """, new { modulo, nombre });

        if (yaEsta is not null)
            throw new InvalidOperationException(
                $"Ya existe una categoría '{nombre}' en {modulo} " +
                $"({(yaEsta.Equals("Sistema", StringComparison.OrdinalIgnoreCase) ? "del sistema" : "tuya")}).");

        // Al final: una categoría nueva no debería reordenar lo que ya se veía.
        var id = await conn.QuerySingleAsync<Guid>($"""
            INSERT INTO [{Schema}].SuggestedCategories (Modulo, Nombre, Origen, Orden)
            OUTPUT INSERTED.Id
            SELECT @modulo, @nombre, 'Usuario',
                   ISNULL((SELECT MAX(Orden) FROM [{Schema}].SuggestedCategories WHERE Modulo = @modulo), 0) + 1
            """, new { modulo, nombre });

        return (await ListarCategoriasAsync(database, modulo)).First(c => c.Id == id);
    }

    /// <summary>
    /// Borra una categoría del usuario. Se rechaza en dos casos, por razones
    /// distintas: si es del sistema no es suya; si tiene preguntas colgando, el
    /// borrado las dejaría en una categoría que ya no existe y desaparecerían de la
    /// pantalla sin que nadie las haya borrado.
    /// </summary>
    public async Task<bool> BorrarCategoriaAsync(string database, Guid id)
    {
        await using var conn = await ConnAsync(database);

        var cat = await conn.QuerySingleOrDefaultAsync<CategoriaRow>($"""
            SELECT c.Id, c.Modulo, c.Nombre, c.Origen, c.Orden, c.CreatedAt, c.CreatedBy,
                   Preguntas = (SELECT COUNT(*) FROM [{Schema}].SuggestedQuestions q
                                 WHERE q.Modulo = c.Modulo AND q.Categoria = c.Nombre)
            FROM [{Schema}].SuggestedCategories c WHERE c.Id = @id
            """, new { id });

        if (cat is null) return false;

        if (cat.Origen.Equals("Sistema", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"La categoría '{cat.Nombre}' es del sistema y no se puede borrar. " +
                "Sólo se borran las que creaste vos.");

        if (cat.Preguntas > 0)
            throw new InvalidOperationException(
                $"La categoría '{cat.Nombre}' tiene {cat.Preguntas} pregunta(s). " +
                "Movelas o borralas primero: si no, quedarían en una categoría inexistente.");

        return await conn.ExecuteAsync(
            $"DELETE FROM [{Schema}].SuggestedCategories WHERE Id = @id", new { id }) > 0;
    }

    private static (string Modulo, string Nombre) ValidarCategoria(string? modulo, string? nombre)
    {
        var m = string.IsNullOrWhiteSpace(modulo) ? "ADPRO" : modulo.Trim().ToUpperInvariant();
        if (m is not ("ADPRO" or "SRM"))
            throw new ArgumentException($"Módulo '{m}' no válido. Se espera ADPRO o SRM.");

        var n = nombre?.Trim() ?? "";
        if (n.Length == 0) throw new ArgumentException("El nombre de la categoría no puede estar vacío.");
        if (n.Length > 50) throw new ArgumentException("El nombre de la categoría no puede superar 50 caracteres.");

        return (m, n);
    }

    /// <summary>
    /// Registra la categoría si no existía. Se llama al crear una pregunta: escribir
    /// una categoría nueva ahí ES crearla, y obligar a crearla aparte primero sería
    /// un paso de más sin ninguna ganancia.
    /// </summary>
    private async Task AsegurarCategoriaAsync(SqlConnection conn, string modulo, string nombre) =>
        await conn.ExecuteAsync($"""
            IF NOT EXISTS (SELECT 1 FROM [{Schema}].SuggestedCategories
                            WHERE Modulo = @modulo AND Nombre = @nombre)
            INSERT INTO [{Schema}].SuggestedCategories (Modulo, Nombre, Origen, Orden)
            SELECT @modulo, @nombre, 'Usuario',
                   ISNULL((SELECT MAX(Orden) FROM [{Schema}].SuggestedCategories WHERE Modulo = @modulo), 0) + 1
            """, new { modulo, nombre });

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
