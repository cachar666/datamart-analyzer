using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace DatamartAnalyzer.Mcp.Http;

/// <summary>
/// Marca de scope: un identificador distinto por cada scope de DI que se cree.
/// No tiene otro proposito que hacer visible algo que normalmente es invisible.
/// </summary>
public sealed class MarcaDeScope
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreadoUtc { get; } = DateTime.UtcNow;
}

/// <summary>
/// Herramienta de diagnostico del SPIKE (fase A1 del plan).
///
/// Contesta la unica pregunta que puede cambiarle el tamano a todo el frente A:
/// ¿el scope de DI del SDK dura lo que la SESION MCP, o lo que el REQUEST HTTP?
///
///   · Dos llamadas seguidas devuelven el MISMO scopeId  -> el scope es por sesion.
///     Registrar Scoped alcanza y el estado de BackendBase se aisla solo.
///   · Devuelven scopeId DISTINTOS                       -> el scope es por request.
///     usar_datamart y fijar_filtros no recordarian nada entre llamadas, y hay que
///     sostener el estado en un almacen indexado por sessionId.
///
/// Esta clase NO viaja al conector local: vive solo en el host HTTP.
/// Se puede quitar cuando el spike este cerrado y anotado.
/// </summary>
[McpServerToolType]
public class DiagnosticoTools(MarcaDeScope marca, IServiceProvider raiz)
{
    private readonly IServiceProvider _raiz = raiz;


    [McpServerTool(Name = "diag_scope")]
    [Description("Diagnóstico interno: devuelve el identificador del scope de inyección de dependencias que atendió esta llamada. Llamarla dos veces seguidas para ver si el scope sobrevive entre llamadas de la misma sesión.")]
    public string DiagScope(McpServer server)
    {
        // La marca inyectada por constructor identifica el scope donde el SDK
        // construyo esta instancia — el mismo scope donde vive BackendSql, que es
        // lo que realmente importa.
        //
        // OJO con server.Services: es el proveedor RAIZ, no un scope de sesion.
        // Resolver un Scoped de ahi lanza "Cannot resolve scoped service from root
        // provider". Por eso aca solo se informa si es el raiz, y no se resuelve nada.
        var esRaiz = ReferenceEquals(server.Services, _raiz);

        // ¿server.Services es un proveedor POR SESION? Si lo es, resolver de ahi
        // devuelve la misma instancia en todas las llamadas de la sesion, y ese es
        // el lugar donde tiene que vivir el estado de usar_datamart y fijar_filtros.
        string? marcaDeServerServices;
        try
        {
            marcaDeServerServices = server.Services?.GetService<MarcaDeScope>()?.Id.ToString() ?? "(no resuelve)";
        }
        catch (Exception ex)
        {
            marcaDeServerServices = $"(lanza: {ex.GetType().Name})";
        }

        return JsonSerializer.Serialize(new
        {
            scopeDeLaHerramienta = marca.Id,
            creadoUtc = marca.CreadoUtc,
            serverServicesEsElProveedorRaiz = esRaiz,
            marcaDesdeServerServices = marcaDeServerServices,
            ahoraUtc = DateTime.UtcNow,
            comoLeerlo =
                "Llamar DOS veces en la misma sesion y comparar scopeDeLaHerramienta. " +
                "Estable = el scope dura lo que la sesion MCP. " +
                "Distinto en cada llamada = dura lo que el request HTTP.",
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
