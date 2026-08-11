using DatamartAnalyzer.Core.Models;
using DatamartAnalyzer.Core.Store;
using DatamartAnalyzer.Core.Prebuilt;
using DatamartAnalyzer.Core.Rag;
using DatamartAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatamartAnalyzer.Api.Controllers;

// ═══════════════════════════════════════════════════════════════════════════════
//  Almacén: consultas guardadas, dashboards y presets de filtros.
//
//  Todo cuelga de {database} porque el almacén vive DENTRO de cada datamart, en el
//  esquema ADP_DTM_APP. Mismo estilo de ruta que /api/schema/{database} y
//  /api/filters/{database}/{tipo}.
//
//  Ejecución: estos controladores NO ejecutan SQL. Para correr una consulta o un
//  panel, el cliente obtiene el SQL por GET y lo manda a POST /api/analyze/execute,
//  que ya resuelve la inyección de filtros y la whitelist de seguridad.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Base común: valida que el datamart exista y tenga el almacén instalado.</summary>
public abstract class StoreControllerBase : ControllerBase
{
    protected readonly IStoreRepository Store;
    private readonly IDatamartConnectionResolver _conexiones;
    private readonly IStoreMigrator _migrador;

    protected StoreControllerBase(
        IStoreRepository store, IDatamartConnectionResolver conexiones, IStoreMigrator migrador)
    {
        Store = store;
        _conexiones = conexiones;
        _migrador = migrador;
    }

    /// <summary>
    /// Devuelve null si todo está bien; si no, el IActionResult de error a retornar.
    /// </summary>
    protected async Task<IActionResult?> ValidarAsync(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
            return BadRequest(new { error = "Se requiere el nombre de la base de datos." });

        // Antes esto abría DOS conexiones antes de hacer nada: una para preguntar si
        // el servidor respondía y otra para ver si el almacén estaba. Pero abrir la
        // segunda YA responde la primera — si conecta, es alcanzable. Contra YOSEMITE
        // el viaje de más es barato; contra un datamart remoto con 40 ms de ida y
        // vuelta se paga en cada request del almacén.
        var (alcanzable, instalado, errorConexion) = await Store.VerificarAlmacenAsync(database);

        if (!alcanzable)
        {
            // No se pudo ni abrir la conexión: eso es "no llego", no "falta instalar".
            // Mandar a alguien a correr el script de instalación acá sería mandarlo a
            // perder el tiempo contra un servidor que no responde.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"No se pudo conectar al datamart '{database}'.",
                detalle = errorConexion,
                servidor = await _conexiones.ServidorDeAsync(database),
                sugerencia = "Revisá que el servidor sea alcanzable desde donde corre la API " +
                             "(firewall, VPN, puerto) y que las credenciales sean correctas."
            });
        }

        if (instalado)
        {
            // El almacén está, pero puede estar VIEJO: HayAlmacenAsync sólo mira las
            // cuatro tablas originales, así que un script nuevo (una tabla agregada
            // en una versión posterior) lo daba por instalado y el endpoint reventaba
            // con 500 al tocar algo que todavía no existía.
            //
            // Aplicar scripts pendientes sobre un almacén YA instalado es distinto de
            // crearlo de cero: no hay decisión que consultarle a nadie, y no hacerlo
            // deja la API rota. Es idempotente y se cachea por proceso.
            var (alDia, errorUpgrade) = await _migrador.AsegurarInstaladoAsync(database);
            if (alDia) return null;

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"El almacén de '{database}' quedó desactualizado y no se pudo poner al día.",
                detalle = errorUpgrade,
                solucion = $"scripts\\almacen.ps1 -Database {database}"
            });
        }

        // Instalar de CERO es otra cosa. En modo ERP el datamart lo dicta
        // dbo.ADPConfig y es EL de esta instalación, así que se crea al primer uso.
        // En standalone no se toca una base sin pedirlo: ahí manda almacen.ps1.
        if (_conexiones.ModoErp)
        {
            var (ok, error) = await _migrador.AsegurarInstaladoAsync(database);
            if (ok) return null;

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"No se pudo instalar el almacén en '{database}'.",
                detalle = error,
                solucion = $"scripts\\almacen.ps1 -Database {database}"
            });
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            error = $"El almacén no está instalado en '{database}'.",
            solucion = $"scripts\\almacen.ps1 -Database {database}"
        });
    }
}

// ─── Preguntas sugeridas ──────────────────────────────────────────────────────

/// <summary>
/// El catálogo de preguntas sugeridas. Vivía hardcodeado en App.jsx y el contador
/// de usos en el localStorage del navegador; ahora vive en el datamart, así lo ven
/// igual la aplicación web y el conector MCP.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SuggestedQuestionsController : StoreControllerBase
{
    public SuggestedQuestionsController(
        IStoreRepository store, IDatamartConnectionResolver conexiones, IStoreMigrator migrador)
        : base(store, conexiones, migrador) { }

    /// <summary>Catálogo. Sin ocultas salvo que se pidan explícitamente.</summary>
    [HttpGet("{database}")]
    public async Task<IActionResult> Listar(
        string database, [FromQuery] string? modulo, [FromQuery] bool incluirOcultas = false)
        => await ValidarAsync(database)
           ?? Ok(await Store.ListarSugeridasAsync(database, modulo, incluirOcultas));

    [HttpPost("{database}")]
    public async Task<IActionResult> Crear(string database, [FromBody] CreateSuggestedQuestionRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        try
        {
            var creada = await Store.CrearSugeridaAsync(database, req);
            return CreatedAtAction(nameof(Listar), new { database }, creada);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Actualiza. De una del sistema sólo se pueden cambiar Activa y Orden — el
    /// repositorio devuelve 409 y explica por qué si se intenta editar su texto.
    /// </summary>
    [HttpPut("{database}/{id:guid}")]
    public async Task<IActionResult> Actualizar(
        string database, Guid id, [FromBody] UpdateSuggestedQuestionRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        try
        {
            var actualizada = await Store.ActualizarSugeridaAsync(database, id, req);
            return actualizada is null
                ? NotFound(new { error = "Pregunta no encontrada." })
                : Ok(actualizada);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Borra una del usuario. Las del sistema se ocultan, no se borran.</summary>
    [HttpDelete("{database}/{id:guid}")]
    public async Task<IActionResult> Borrar(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        try
        {
            return await Store.BorrarSugeridaAsync(database, id)
                ? NoContent()
                : NotFound(new { error = "Pregunta no encontrada." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message, solucion = "Ocultala con Activa = false." });
        }
    }

    // ── Categorías ───────────────────────────────────────────────────────────

    [HttpGet("{database}/categorias")]
    public async Task<IActionResult> ListarCategorias(string database, [FromQuery] string? modulo)
        => await ValidarAsync(database) ?? Ok(await Store.ListarCategoriasAsync(database, modulo));

    [HttpPost("{database}/categorias")]
    public async Task<IActionResult> CrearCategoria(
        string database, [FromBody] CreateSuggestedCategoryRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        try { return Ok(await Store.CrearCategoriaAsync(database, req)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>
    /// Borra una categoría propia. Las del sistema no se borran, y una que todavía
    /// tenga preguntas tampoco: quedarían apuntando a algo inexistente.
    /// </summary>
    [HttpDelete("{database}/categorias/{id:guid}")]
    public async Task<IActionResult> BorrarCategoria(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        try
        {
            return await Store.BorrarCategoriaAsync(database, id)
                ? NoContent()
                : NotFound(new { error = "Categoría no encontrada." });
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Registra que alguien la usó. Alimenta el Top del equipo y el propio.</summary>
    [HttpPost("{database}/{id:guid}/uso")]
    public async Task<IActionResult> RegistrarUso(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        return await Store.RegistrarUsoSugeridaAsync(database, id)
            ? NoContent()
            : NotFound(new { error = "Pregunta no encontrada." });
    }
}

// ─── Consultas guardadas ──────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class SavedQueriesController : StoreControllerBase
{
    public SavedQueriesController(IStoreRepository store, IDatamartConnectionResolver conexiones, IStoreMigrator migrador)
        : base(store, conexiones, migrador) { }

    [HttpGet("{database}")]
    public async Task<IActionResult> Listar(string database, [FromQuery] string? buscar)
        => await ValidarAsync(database) ?? Ok(await Store.ListarConsultasAsync(database, buscar));

    [HttpGet("{database}/{id:guid}")]
    public async Task<IActionResult> Obtener(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        var consulta = await Store.ObtenerConsultaAsync(database, id);
        return consulta is null ? NotFound(new { error = "Consulta no encontrada." }) : Ok(consulta);
    }

    [HttpPost("{database}")]
    public async Task<IActionResult> Crear(string database, [FromBody] CreateSavedQueryRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        if (string.IsNullOrWhiteSpace(req.Pregunta))
            return BadRequest(new { error = "Se requiere pregunta." });
        if (string.IsNullOrWhiteSpace(req.Sql))
            return BadRequest(new { error = "Se requiere sql." });

        var creada = await Store.CrearConsultaAsync(database, req);
        return CreatedAtAction(nameof(Obtener), new { database, id = creada.Id }, creada);
    }

    [HttpPut("{database}/{id:guid}")]
    public async Task<IActionResult> Actualizar(string database, Guid id, [FromBody] UpdateSavedQueryRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        var actualizada = await Store.ActualizarConsultaAsync(database, id, req);
        return actualizada is null ? NotFound(new { error = "Consulta no encontrada." }) : Ok(actualizada);
    }

    [HttpDelete("{database}/{id:guid}")]
    public async Task<IActionResult> Borrar(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        return await Store.BorrarConsultaAsync(database, id)
            ? NoContent()
            : NotFound(new { error = "Consulta no encontrada." });
    }
}

// ─── Dashboards ───────────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class DashboardsController : StoreControllerBase
{
    public DashboardsController(IStoreRepository store, IDatamartConnectionResolver conexiones, IStoreMigrator migrador)
        : base(store, conexiones, migrador) { }

    [HttpGet("{database}")]
    public async Task<IActionResult> Listar(string database)
        => await ValidarAsync(database) ?? Ok(await Store.ListarDashboardsAsync(database));

    [HttpGet("{database}/{id:guid}")]
    public async Task<IActionResult> Obtener(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        var dash = await Store.ObtenerDashboardAsync(database, id);
        return dash is null ? NotFound(new { error = "Dashboard no encontrado." }) : Ok(dash);
    }

    [HttpPost("{database}")]
    public async Task<IActionResult> Crear(string database, [FromBody] CreateDashboardRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        if (string.IsNullOrWhiteSpace(req.Nombre))
            return BadRequest(new { error = "Se requiere nombre." });

        try
        {
            var creado = await Store.CrearDashboardAsync(database, req);
            return CreatedAtAction(nameof(Obtener), new { database, id = creado.Id }, creado);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(new { error = $"Ya existe un dashboard llamado '{req.Nombre}' en {database}." });
        }
    }

    [HttpPut("{database}/{id:guid}")]
    public async Task<IActionResult> Actualizar(string database, Guid id, [FromBody] UpdateDashboardRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        try
        {
            var actualizado = await Store.ActualizarDashboardAsync(database, id, req);
            return actualizado is null ? NotFound(new { error = "Dashboard no encontrado." }) : Ok(actualizado);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(new { error = $"Ya existe un dashboard llamado '{req.Nombre}'." });
        }
    }

    [HttpDelete("{database}/{id:guid}")]
    public async Task<IActionResult> Borrar(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        return await Store.BorrarDashboardAsync(database, id)
            ? NoContent()
            : NotFound(new { error = "Dashboard no encontrado." });
    }

    // ── Paneles ──────────────────────────────────────────────────────────────

    [HttpPost("{database}/{id:guid}/panels")]
    public async Task<IActionResult> AgregarPanel(string database, Guid id, [FromBody] AddPanelRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        if (string.IsNullOrWhiteSpace(req.Titulo))
            return BadRequest(new { error = "Se requiere titulo." });

        var tieneSaved = req.SavedQueryId is not null;
        var tieneSql = !string.IsNullOrWhiteSpace(req.Sql);
        if (tieneSaved == tieneSql)
            return BadRequest(new { error = "Enviá savedQueryId o sql — exactamente uno de los dos." });

        var panel = await Store.AgregarPanelAsync(database, id, req);
        return panel is null
            ? NotFound(new { error = "Dashboard o consulta guardada no encontrados." })
            : Ok(panel);
    }

    [HttpPut("{database}/{id:guid}/panels/{panelId:guid}")]
    public async Task<IActionResult> ActualizarPanel(
        string database, Guid id, Guid panelId, [FromBody] UpdatePanelRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        var panel = await Store.ActualizarPanelAsync(database, id, panelId, req);
        return panel is null ? NotFound(new { error = "Panel no encontrado." }) : Ok(panel);
    }

    [HttpDelete("{database}/{id:guid}/panels/{panelId:guid}")]
    public async Task<IActionResult> QuitarPanel(string database, Guid id, Guid panelId)
    {
        if (await ValidarAsync(database) is { } error) return error;

        return await Store.QuitarPanelAsync(database, id, panelId)
            ? NoContent()
            : NotFound(new { error = "Panel no encontrado." });
    }

    [HttpPut("{database}/{id:guid}/panels/order")]
    public async Task<IActionResult> Reordenar(string database, Guid id, [FromBody] ReorderPanelsRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        if (req.PanelIds is null || req.PanelIds.Count == 0)
            return BadRequest(new { error = "Se requiere panelIds." });

        return await Store.ReordenarPanelesAsync(database, id, req.PanelIds)
            ? NoContent()
            : BadRequest(new { error = "La lista debe incluir todos los paneles del dashboard, sin repetidos." });
    }
}

// ─── Presets de filtros ───────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class FilterPresetsController : StoreControllerBase
{
    public FilterPresetsController(IStoreRepository store, IDatamartConnectionResolver conexiones, IStoreMigrator migrador)
        : base(store, conexiones, migrador) { }

    [HttpGet("{database}")]
    public async Task<IActionResult> Listar(string database)
        => await ValidarAsync(database) ?? Ok(await Store.ListarPresetsAsync(database));

    [HttpPost("{database}")]
    public async Task<IActionResult> Crear(string database, [FromBody] CreateFilterPresetRequest req)
    {
        if (await ValidarAsync(database) is { } error) return error;

        if (string.IsNullOrWhiteSpace(req.Nombre))
            return BadRequest(new { error = "Se requiere nombre." });

        try
        {
            return Ok(await Store.CrearPresetAsync(database, req));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(new { error = $"Ya existe un preset llamado '{req.Nombre}' en {database}." });
        }
    }

    [HttpDelete("{database}/{id:guid}")]
    public async Task<IActionResult> Borrar(string database, Guid id)
    {
        if (await ValidarAsync(database) is { } error) return error;

        return await Store.BorrarPresetAsync(database, id)
            ? NoContent()
            : NotFound(new { error = "Preset no encontrado." });
    }
}

// ─── Estado e importación ─────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class StoreController : ControllerBase
{
    private readonly IStoreRepository _store;
    private readonly IDatamartConnectionResolver _conexiones;
    private readonly IStoreMigrator _migrador;

    public StoreController(
        IStoreRepository store, IDatamartConnectionResolver conexiones, IStoreMigrator migrador)
    {
        _store = store;
        _conexiones = conexiones;
        _migrador = migrador;
    }

    /// <summary>
    /// ¿Esta base tiene el almacén instalado y cuánto hay dentro?
    /// El frontend lo consulta al elegir base, para saber si ofrecer la importación
    /// de lo que tenga en localStorage o avisar que faltan las migraciones.
    /// </summary>
    [HttpGet("status/{database}")]
    public async Task<IActionResult> Status(string database)
    {
        // EsAlcanzableAsync y no "existe en YOSEMITE": en modo ERP el datamart puede
        // vivir en otro servidor, y el catálogo local no sabe nada de él.
        if (!await _conexiones.EsAlcanzableAsync(database))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"No se pudo conectar al datamart '{database}'.",
                servidor = await _conexiones.ServidorDeAsync(database),
                sugerencia = "Revisá que el servidor sea alcanzable desde donde corre la API " +
                             "(firewall, VPN, puerto) y que las credenciales sean correctas."
            });

        return Ok(await _store.EstadoAsync(database));
    }

    /// <summary>
    /// Importa el volcado de localStorage a la base indicada. Idempotente: deduplica
    /// consultas por SQL y dashboards por nombre, y nunca pisa lo que ya existe.
    /// </summary>
    [HttpPost("import/{database}")]
    public async Task<IActionResult> Importar(string database, [FromBody] ImportRequest req)
    {
        // EsAlcanzableAsync y no "existe en YOSEMITE": en modo ERP el datamart puede
        // vivir en otro servidor, y el catálogo local no sabe nada de él.
        if (!await _conexiones.EsAlcanzableAsync(database))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"No se pudo conectar al datamart '{database}'.",
                servidor = await _conexiones.ServidorDeAsync(database),
                sugerencia = "Revisá que el servidor sea alcanzable desde donde corre la API " +
                             "(firewall, VPN, puerto) y que las credenciales sean correctas."
            });

        if (!await _store.HayAlmacenAsync(database))
        {
            bool ok;
            string? error;

            if (_conexiones.ModoErp)
                (ok, error) = await _migrador.AsegurarInstaladoAsync(database);
            else
                (ok, error) = (false, "Modo standalone: instalalo con scripts\\almacen.ps1.");

            if (!ok)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = $"El almacén no está instalado en '{database}'.",
                    detalle = error,
                    solucion = $"scripts\\almacen.ps1 -Database {database}"
                });
        }

        return Ok(await _store.ImportarAsync(database, req));
    }
}
