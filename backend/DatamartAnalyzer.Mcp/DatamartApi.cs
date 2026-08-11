using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatamartAnalyzer.Mcp;

/// <summary>
/// Cliente de la API de Datamart Analyzer.
///
/// Resuelve el datamart activo en este orden: lo que pida la herramienta, lo que
/// se haya fijado con usar_datamart, DATAMART_DB, y por último lo que declare el
/// ERP en dbo.ADPConfig. Así el conector funciona igual embebido en un ERP que
/// apuntando a un datamart suelto.
/// </summary>
public class DatamartApi(HttpClient http, McpOptions opciones)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Los enums viajan como texto, igual que en la API. Sin esto TipoGrafico.Torta
        // se manda como 2 y lo que se lee de vuelta no se parece a lo que se mando.
        Converters = { new JsonStringEnumConverter() }
    };

    public string ApiUrl => opciones.ApiUrl;

    // El datamart activo y los filtros de la conversación NO viven acá: son estado
    // de sesión y viven en BackendBase, que es común a los dos modos. Tenerlos en
    // los dos lados era garantía de que un día se desincronizaran.

    /// <summary>
    /// Datamart efectivo cuando nadie lo fijó: lo que diga la configuración o, en
    /// último caso, lo que declare el ERP. Lanza con un mensaje accionable si no hay
    /// ninguno, en vez de fallar con un 404 opaco más adelante.
    /// </summary>
    public async Task<string> ResolverDatamartAsync(string? pedido = null)
    {
        if (!string.IsNullOrWhiteSpace(pedido)) return pedido;
        if (!string.IsNullOrWhiteSpace(opciones.DatamartPorDefecto)) return opciones.DatamartPorDefecto;

        var ctx = await GetAsync<ErpDatamarts>("erp/datamarts");
        var unico = ctx?.Datamarts?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(unico)) return unico;

        throw new InvalidOperationException(
            "No hay datamart definido. Usá la herramienta usar_datamart, o configurá " +
            "DATAMART_DB en claude_desktop_config.json, o declaralo en dbo.ADPConfig del ERP.");
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(string ruta)
    {
        var r = await http.GetAsync(ruta);
        await LanzarSiFalla(r, ruta);
        return await r.Content.ReadFromJsonAsync<T>(Json);
    }

    public async Task<T?> PostAsync<T>(string ruta, object? cuerpo = null)
    {
        var r = await http.PostAsJsonAsync(ruta, cuerpo ?? new { }, Json);
        await LanzarSiFalla(r, ruta);
        if (r.StatusCode == HttpStatusCode.NoContent) return default;
        return await r.Content.ReadFromJsonAsync<T>(Json);
    }

    public async Task<T?> PutAsync<T>(string ruta, object cuerpo)
    {
        var r = await http.PutAsJsonAsync(ruta, cuerpo, Json);
        await LanzarSiFalla(r, ruta);
        if (r.StatusCode == HttpStatusCode.NoContent) return default;
        return await r.Content.ReadFromJsonAsync<T>(Json);
    }

    public async Task DeleteAsync(string ruta)
    {
        var r = await http.DeleteAsync(ruta);
        await LanzarSiFalla(r, ruta);
    }

    /// <summary>
    /// Traduce el error de la API a algo accionable. La API devuelve
    /// { error, detalle, solucion } — sobre todo en el 503 de "almacén no instalado",
    /// donde `solucion` trae el comando exacto.
    /// </summary>
    private static async Task LanzarSiFalla(HttpResponseMessage r, string ruta)
    {
        if (r.IsSuccessStatusCode) return;

        var texto = await r.Content.ReadAsStringAsync();
        string mensaje;
        try
        {
            var d = JsonSerializer.Deserialize<ErrorApi>(texto, Json);
            mensaje = d?.Error ?? texto;
            if (!string.IsNullOrWhiteSpace(d?.Detalle)) mensaje += $" — {d.Detalle}";
            if (!string.IsNullOrWhiteSpace(d?.Solucion)) mensaje += $"\nSolución: {d.Solucion}";
            if (!string.IsNullOrWhiteSpace(d?.Servidor)) mensaje += $"\nServidor: {d.Servidor}";
        }
        catch
        {
            mensaje = string.IsNullOrWhiteSpace(texto) ? r.ReasonPhrase ?? "sin detalle" : texto;
        }

        throw new InvalidOperationException($"[{(int)r.StatusCode}] {ruta}: {mensaje}");
    }

    private record ErrorApi(string? Error, string? Detalle, string? Solucion, string? Servidor);
    private record ErpDatamarts(string Modo, string? ErpDatabase, List<string>? Datamarts, string? Mensaje);
}
