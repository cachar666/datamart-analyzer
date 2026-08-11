using System.Text.Json;

namespace DatamartAnalyzer.Mcp.Backends;

/// <summary>
/// Habla con la API REST del Datamart Analyzer. Es el modo preferido cuando existe:
/// hereda el modo ERP (el datamart sale de dbo.ADPConfig), la generacion de SQL con
/// IA y el almacen ya instalado.
/// </summary>
public sealed class BackendApi(DatamartApi api) : BackendBase
{
    public override string Modo => "api";
    public override string Descripcion => $"API {api.ApiUrl}";

    public override async Task<string> ResolverDatamartAsync(string? pedido = null)
    {
        if (!string.IsNullOrWhiteSpace(pedido)) return pedido;
        if (!string.IsNullOrWhiteSpace(DatamartDeSesion)) return DatamartDeSesion;
        return await api.ResolverDatamartAsync();
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    public override async Task<JsonElement> ContextoAsync() =>
        await api.GetAsync<JsonElement>("erp/datamarts");

    public override async Task<JsonElement> EstadoAlmacenAsync(string db) =>
        await api.GetAsync<JsonElement>($"store/status/{E(db)}");

    public override async Task<JsonElement> EsquemaAsync(string db) =>
        await api.GetAsync<JsonElement>($"schema/{E(db)}");

    public override async Task<JsonElement> ValoresFiltroAsync(string db, string tipo) =>
        await api.GetAsync<JsonElement>($"filters/{E(db)}/{E(tipo)}");

    public override async Task<JsonElement> PreguntarAsync(string db, string pregunta)
    {
        var esquema = await EsquemaAsync(db);

        var payload = new
        {
            database = db,
            pregunta,
            schema = esquema.TryGetProperty("columnas", out var c) ? c : default,
            vistas = esquema.TryGetProperty("vistas", out var v) ? v : default,
            contextoVariables = FiltrosONull,
        };

        return await api.PostAsync<JsonElement>("analyze", payload);
    }

    public override async Task<JsonElement> EjecutarSqlAsync(string db, string sql, bool aplicarFiltros = true) =>
        await api.PostAsync<JsonElement>("analyze/execute", new
        {
            database = db,
            sql,
            filtros = aplicarFiltros ? FiltrosONull : null,
        });

    public override async Task<JsonElement> ListarSugeridasAsync(string db, string? modulo)
    {
        var ruta = $"suggestedquestions/{E(db)}";
        if (!string.IsNullOrWhiteSpace(modulo)) ruta += $"?modulo={E(modulo)}";
        return await api.GetAsync<JsonElement>(ruta);
    }

    public override async Task<JsonElement> CrearSugeridaAsync(
        string db, string pregunta, string categoria, string modulo) =>
        await api.PostAsync<JsonElement>($"suggestedquestions/{E(db)}",
            new { pregunta, categoria, modulo });

    public override Task BorrarSugeridaAsync(string db, string id) =>
        api.DeleteAsync($"suggestedquestions/{E(db)}/{id}");

    public override Task RegistrarUsoSugeridaAsync(string db, string id) =>
        api.PostAsync<JsonElement>($"suggestedquestions/{E(db)}/{id}/uso");

    public override async Task<JsonElement> ListarCategoriasAsync(string db, string? modulo)
    {
        var ruta = $"suggestedquestions/{E(db)}/categorias";
        if (!string.IsNullOrWhiteSpace(modulo)) ruta += $"?modulo={E(modulo)}";
        return await api.GetAsync<JsonElement>(ruta);
    }

    public override async Task<JsonElement> CrearCategoriaAsync(string db, string nombre, string modulo) =>
        await api.PostAsync<JsonElement>($"suggestedquestions/{E(db)}/categorias", new { nombre, modulo });

    public override Task BorrarCategoriaAsync(string db, string id) =>
        api.DeleteAsync($"suggestedquestions/{E(db)}/categorias/{id}");

    public override async Task<JsonElement> ListarConsultasAsync(string db, string? buscar)
    {
        var ruta = $"savedqueries/{E(db)}";
        if (!string.IsNullOrWhiteSpace(buscar)) ruta += $"?buscar={E(buscar)}";
        return await api.GetAsync<JsonElement>(ruta);
    }

    public override async Task<JsonElement> ObtenerConsultaAsync(string db, string id) =>
        await api.GetAsync<JsonElement>($"savedqueries/{E(db)}/{id}");

    public override async Task<JsonElement> GuardarConsultaAsync(
        string db, string pregunta, string sql, string tipoRespuesta, string? tags, GraficoPedido? grafico)
    {
        var g = await PrepararGraficoAsync(db, sql, null, grafico);
        return await api.PostAsync<JsonElement>($"savedqueries/{E(db)}",
            new { pregunta, sql, tipoRespuesta, tags, grafico = g });
    }

    public override Task BorrarConsultaAsync(string db, string id) =>
        api.DeleteAsync($"savedqueries/{E(db)}/{id}");

    public override async Task<JsonElement> ListarDashboardsAsync(string db) =>
        await api.GetAsync<JsonElement>($"dashboards/{E(db)}");

    public override async Task<JsonElement> VerDashboardAsync(string db, string id) =>
        await api.GetAsync<JsonElement>($"dashboards/{E(db)}/{id}");

    public override async Task<JsonElement> CrearDashboardAsync(string db, string nombre, string? descripcion) =>
        await api.PostAsync<JsonElement>($"dashboards/{E(db)}", new { nombre, descripcion });

    public override async Task<JsonElement> ActualizarDashboardAsync(
        string db, string id, string? nombre, string? descripcion) =>
        await api.PutAsync<JsonElement>($"dashboards/{E(db)}/{id}", new { nombre, descripcion });

    public override Task BorrarDashboardAsync(string db, string id) =>
        api.DeleteAsync($"dashboards/{E(db)}/{id}");

    public override async Task<JsonElement> AgregarPanelAsync(
        string db, string dashboardId, string titulo, string? consultaId, string? sql, string tipoRespuesta,
        GraficoPedido? grafico)
    {
        var g = await PrepararGraficoAsync(db, sql, consultaId, grafico);
        return await api.PostAsync<JsonElement>($"dashboards/{E(db)}/{dashboardId}/panels",
            new { titulo, savedQueryId = consultaId, sql, tipoRespuesta, grafico = g });
    }

    public override Task QuitarPanelAsync(string db, string dashboardId, string panelId) =>
        api.DeleteAsync($"dashboards/{E(db)}/{dashboardId}/panels/{panelId}");
}
