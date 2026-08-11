namespace DatamartAnalyzer.Core.Store;

/// <summary>
/// Como se llega a un datamart. Existe para que el almacen no dependa de COMO se
/// resuelve la conexion: la API la deriva de appsettings o de dbo.ADPConfig, y el
/// conector MCP la arma con las credenciales que el usuario cargo al instalarlo.
/// </summary>
public interface IConexionDatamart
{
    /// <summary>Cadena de conexion lista para usar contra ese datamart.</summary>
    Task<string> ResolverAsync(string database);
}

/// <summary>
/// Implementacion directa a partir de los cuatro datos que se le entregan al
/// usuario final: servidor, base, usuario y clave.
/// </summary>
public sealed class ConexionFija(string connectionString) : IConexionDatamart
{
    public Task<string> ResolverAsync(string database) => Task.FromResult(connectionString);
}
