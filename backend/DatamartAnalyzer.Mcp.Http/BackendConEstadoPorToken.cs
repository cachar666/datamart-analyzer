using System.Text.Json;
using DatamartAnalyzer.Mcp.Backends;

namespace DatamartAnalyzer.Mcp.Http;

/// <summary>
/// Le devuelve memoria al backend sobre HTTP.
///
/// EL PROBLEMA. BackendSql guarda el datamart activo y los filtros en campos de
/// instancia. En stdio eso alcanza porque hay un proceso por cliente. Sobre HTTP se
/// midio que el scope de DI se recrea en CADA tool call — incluso con Mcp-Session-Id
/// estable — asi que cada llamada recibiria un BackendSql recien nacido y vacio:
/// fijar_filtros no tendria ningun efecto sobre la llamada siguiente.
///
/// POR QUE UN DECORADOR Y NO UNA SUBCLASE. Los metodos de sesion de BackendBase
/// (FijarDatamart, FijarFiltros, LimpiarFiltros) no son virtuales, y el conector
/// local NO se modifica: es una decision explicita del plan. Envolver es la unica
/// via que respeta las dos cosas.
///
/// COMO FUNCIONA. Al construirse —una vez por llamada— hidrata el BackendSql interno
/// con lo que hay guardado para ese token. Cuando una herramienta cambia el estado,
/// escribe en los dos lados: en el almacen, para que sobreviva a la llamada, y en el
/// interno, para que la logica de BackendSql —que lee sus propios campos protegidos—
/// funcione sin enterarse de nada.
///
/// Todo lo demas delega tal cual. Es verboso a proposito: una firma que se desviara
/// de la del contrato seria un cambio de comportamiento silencioso entre el conector
/// local y el remoto.
/// </summary>
public sealed class BackendConEstadoPorToken : IBackend
{
    private readonly IBackend _interno;
    private readonly EstadoSesion _estado;

    public BackendConEstadoPorToken(BackendSql interno, EstadoSesion estado)
    {
        _interno = interno;
        _estado = estado;

        // Hidratacion. Sin esto, cada llamada empezaria en blanco.
        if (!string.IsNullOrWhiteSpace(_estado.Datamart))
            _interno.FijarDatamart(_estado.Datamart);

        if (_estado.Filtros.Count > 0)
            _interno.FijarFiltros(_estado.CopiaDeFiltros());
    }

    // ── Estado de sesion: lo unico que este decorador hace de verdad ──────────

    public void FijarDatamart(string nombre)
    {
        _estado.Datamart = nombre;
        _estado.Tocar();
        _interno.FijarDatamart(nombre);
    }

    public void FijarFiltros(Dictionary<string, List<string>>? filtros)
    {
        _estado.ReemplazarFiltros(filtros);
        _interno.FijarFiltros(filtros);
    }

    public void LimpiarFiltros()
    {
        _estado.LimpiarFiltros();
        _interno.LimpiarFiltros();
    }

    // Se lee del interno y no del almacen: asi lo que reporta la herramienta es
    // exactamente lo que se va a aplicar en la consulta, y no una segunda version
    // de la verdad que algun dia se desincroniza.
    public IReadOnlyDictionary<string, List<string>> Filtros => _interno.Filtros;

    // ── Delegacion pura ──────────────────────────────────────────────────────

    public string Modo => _interno.Modo;
    public string Descripcion => _interno.Descripcion;

    public Task<string> ResolverDatamartAsync(string? pedido = null) =>
        _interno.ResolverDatamartAsync(pedido);

    public Task<JsonElement> ContextoAsync() => _interno.ContextoAsync();
    public Task<JsonElement> EstadoAlmacenAsync(string db) => _interno.EstadoAlmacenAsync(db);
    public Task<JsonElement> EsquemaAsync(string db) => _interno.EsquemaAsync(db);
    public Task<JsonElement> ValoresFiltroAsync(string db, string tipo) => _interno.ValoresFiltroAsync(db, tipo);

    public Task<JsonElement> PreguntarAsync(string db, string pregunta) => _interno.PreguntarAsync(db, pregunta);

    public Task<JsonElement> EjecutarSqlAsync(string db, string sql, bool aplicarFiltros = true) =>
        _interno.EjecutarSqlAsync(db, sql, aplicarFiltros);

    public Task<JsonElement> ListarSugeridasAsync(string db, string? modulo) =>
        _interno.ListarSugeridasAsync(db, modulo);

    public Task<JsonElement> CrearSugeridaAsync(string db, string pregunta, string categoria, string modulo) =>
        _interno.CrearSugeridaAsync(db, pregunta, categoria, modulo);

    public Task BorrarSugeridaAsync(string db, string id) => _interno.BorrarSugeridaAsync(db, id);
    public Task RegistrarUsoSugeridaAsync(string db, string id) => _interno.RegistrarUsoSugeridaAsync(db, id);

    public Task<JsonElement> ListarCategoriasAsync(string db, string? modulo) =>
        _interno.ListarCategoriasAsync(db, modulo);

    public Task<JsonElement> CrearCategoriaAsync(string db, string nombre, string modulo) =>
        _interno.CrearCategoriaAsync(db, nombre, modulo);

    public Task BorrarCategoriaAsync(string db, string id) => _interno.BorrarCategoriaAsync(db, id);

    public Task<JsonElement> ListarConsultasAsync(string db, string? buscar) =>
        _interno.ListarConsultasAsync(db, buscar);

    public Task<JsonElement> ObtenerConsultaAsync(string db, string id) => _interno.ObtenerConsultaAsync(db, id);

    public Task<JsonElement> GuardarConsultaAsync(string db, string pregunta, string sql, string tipoRespuesta,
                                                  string? tags, GraficoPedido? grafico) =>
        _interno.GuardarConsultaAsync(db, pregunta, sql, tipoRespuesta, tags, grafico);

    public Task BorrarConsultaAsync(string db, string id) => _interno.BorrarConsultaAsync(db, id);

    public Task<JsonElement> ListarDashboardsAsync(string db) => _interno.ListarDashboardsAsync(db);
    public Task<JsonElement> VerDashboardAsync(string db, string id) => _interno.VerDashboardAsync(db, id);

    public Task<JsonElement> CrearDashboardAsync(string db, string nombre, string? descripcion) =>
        _interno.CrearDashboardAsync(db, nombre, descripcion);

    public Task<JsonElement> ActualizarDashboardAsync(string db, string id, string? nombre, string? descripcion) =>
        _interno.ActualizarDashboardAsync(db, id, nombre, descripcion);

    public Task BorrarDashboardAsync(string db, string id) => _interno.BorrarDashboardAsync(db, id);

    public Task<JsonElement> AgregarPanelAsync(string db, string dashboardId, string titulo,
                                               string? consultaId, string? sql, string tipoRespuesta,
                                               GraficoPedido? grafico) =>
        _interno.AgregarPanelAsync(db, dashboardId, titulo, consultaId, sql, tipoRespuesta, grafico);

    public Task QuitarPanelAsync(string db, string dashboardId, string panelId) =>
        _interno.QuitarPanelAsync(db, dashboardId, panelId);
}
