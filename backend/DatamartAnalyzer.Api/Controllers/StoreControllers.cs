using DatamartAnalyzer.Api.Models;
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

        // EsAlcanzableAsync y no "existe en YOSEMITE": en modo ERP el datamart puede
        // vivir en otro servidor, y el catálogo local no sabe nada de él.
        if (!await _conexiones.EsAlcanzableAsync(database))
            return NotFound(new
            {
                error = $"No se pudo conectar al datamart '{database}'.",
                servidor = await _conexiones.ServidorDeAsync(database)
            });

        if (await Store.HayAlmacenAsync(database)) return null;

        // Modo ERP: el datamart lo dicta dbo.ADPConfig y es EL de esta instalación,
        // así que se instala solo al primer uso. En standalone no se toca nada sin
        // pedirlo: ahí manda scripts/almacen.ps1.
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
            return NotFound(new
            {
                error = $"No se pudo conectar al datamart '{database}'.",
                servidor = await _conexiones.ServidorDeAsync(database)
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
            return NotFound(new
            {
                error = $"No se pudo conectar al datamart '{database}'.",
                servidor = await _conexiones.ServidorDeAsync(database)
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
