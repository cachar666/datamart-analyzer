using DatamartAnalyzer.Core.Models;
using DatamartAnalyzer.Core.Store;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Core.Sql;

/// <summary>Limites de ejecucion. Vienen de appsettings en la API y de valores fijos
/// en el conector, pero la regla es la misma en los dos.</summary>
public sealed class SqlLimites
{
    public int TimeoutSegundos { get; init; } = 30;
    public int MaxFilas { get; init; } = 5000;
}

/// <summary>
/// Ejecuta SELECT contra un datamart: valida, inyecta filtros, acota filas y lee.
///
/// Es el UNICO camino por el que sale una consulta hacia SQL Server, venga de la
/// API, de un panel de dashboard o del conector MCP. Antes cada uno tenia su
/// propia version de "primero valido, despues ejecuto" y solo una de ellas
/// aprendia cuando se tapaba un agujero.
/// </summary>
public interface ISqlExecutor
{
    Task<QueryResponse> EjecutarAsync(
        string database, string sql,
        IReadOnlyDictionary<string, List<string>>? filtros = null);
}

public class SqlExecutor(IConexionDatamart conexiones, SqlLimites limites, ILogger<SqlExecutor> logger)
    : ISqlExecutor
{
    public async Task<QueryResponse> EjecutarAsync(
        string database, string sql,
        IReadOnlyDictionary<string, List<string>>? filtros = null)
    {
        var conFiltros = FilterInjector.Inyectar(sql, filtros);

        if (!SqlSafety.EsSeguro(conFiltros, out var motivo))
            return new QueryResponse(false, null, 0, motivo, 0);

        var final = SqlSafety.LimitarFilas(conFiltros, limites.MaxFilas);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(await conexiones.ResolverAsync(database));
            await conn.OpenAsync();

            using var cmd = new SqlCommand(final, conn) { CommandTimeout = limites.TimeoutSegundos };
            using var reader = await cmd.ExecuteReaderAsync();

            var columnas = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var filas = new List<Dictionary<string, object?>>();

            // Tope duro del lado del lector. LimitarFilas no le pone TOP a un WITH
            // (el SELECT final puede estar mucho mas abajo y el TOP terminaria en el
            // CTE equivocado), asi que la CTE de medio millon de filas se corta aca
            // y no en la memoria del proceso.
            while (filas.Count < limites.MaxFilas && await reader.ReadAsync())
            {
                var fila = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                    fila[columnas[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                filas.Add(fila);
            }

            sw.Stop();

            if (filas.Count == limites.MaxFilas)
                logger.LogWarning("Consulta en {db} alcanzo el tope de {max} filas; el resultado esta recortado.",
                    database, limites.MaxFilas);

            logger.LogInformation("Consulta en {db}: {filas} filas en {ms}ms",
                database, filas.Count, sw.ElapsedMilliseconds);

            return new QueryResponse(true, filas, filas.Count, null, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Error ejecutando consulta en {db}", database);
            return new QueryResponse(false, null, 0, ex.Message, sw.Elapsed.TotalMilliseconds);
        }
    }
}
