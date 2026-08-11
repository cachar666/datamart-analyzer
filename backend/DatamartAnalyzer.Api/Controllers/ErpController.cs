using DatamartAnalyzer.Core.Models;
using DatamartAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatamartAnalyzer.Api.Controllers;

/// <summary>
/// Contexto del ERP desde el que se abrió la aplicación.
///
/// El frontend llama a GET /api/erp/context al arrancar:
///   · DesdeErp = true  y DatamartExiste = true  → fija ese datamart y oculta el selector.
///   · DesdeErp = true  y DatamartExiste = false → muestra el Mensaje (falta el registro
///     en dbo.ADPConfig, o la base declarada no existe).
///   · DesdeErp = false → modo standalone: sigue el comportamiento de hoy, se listan
///     todos los datamarts del servidor.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ErpController : ControllerBase
{
    private readonly IErpContextService _erp;
    private readonly ISqlServerService _sql;

    public ErpController(IErpContextService erp, ISqlServerService sql)
    {
        _erp = erp;
        _sql = sql;
    }

    [HttpGet("context")]
    public async Task<IActionResult> Context() => Ok(await _erp.ResolverAsync());

    /// <summary>
    /// Los datamarts que esta instalación puede usar.
    ///
    /// En modo ERP devuelve solo el declarado en dbo.ADPConfig; en standalone,
    /// todos los datamarts del servidor. El frontend puede llamar siempre a este
    /// endpoint en vez de decidir por su cuenta entre uno y otro.
    /// </summary>
    [HttpGet("datamarts")]
    public async Task<IActionResult> Datamarts()
    {
        var ctx = await _erp.ResolverAsync();

        if (!ctx.DesdeErp)
        {
            var todas = await _sql.ObtenerBasesDatosAsync();
            return Ok(new
            {
                modo = "standalone",
                erpServidor = (string?)null,
                erpDatabase = (string?)null,
                datamartServidor = (string?)null,
                datamarts = todas.Where(d => d.EsDatamart).Select(d => d.Nombre).ToList(),
                mensaje = (string?)null
            });
        }

        return Ok(new
        {
            modo = "erp",
            erpServidor = ctx.ErpServidor,
            erpDatabase = ctx.ErpDatabase,
            datamartServidor = ctx.DatamartServidor,
            datamarts = ctx.DatamartExiste && ctx.Datamart is not null
                ? new List<string> { ctx.Datamart }
                : new List<string>(),
            mensaje = ctx.Mensaje
        });
    }

    /// <summary>
    /// Vuelve a leer dbo.ADPConfig. El contexto se cachea al primer uso; esto sirve
    /// cuando el DBA acaba de declarar el datamart y no querés reiniciar el backend.
    /// </summary>
    [HttpPost("context/refresh")]
    public async Task<IActionResult> Refresh()
    {
        _erp.InvalidarCache();
        return Ok(await _erp.ResolverAsync());
    }
}
