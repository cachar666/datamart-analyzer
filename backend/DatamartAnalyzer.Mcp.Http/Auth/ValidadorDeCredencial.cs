using DatamartAnalyzer.Mcp.Backends;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Mcp.Http.Auth;

/// <summary>
/// Comprueba que los cuatro datos sirven, abriendo una conexion de verdad.
///
/// Por que abrir la conexion y no solo validar el formato: si aceptaramos la
/// credencial sin probarla, el usuario terminaria el flujo de OAuth creyendo que
/// quedo conectado y descubriria el error mucho despues, dentro de una conversacion,
/// con un mensaje de SQL que no dice que hacer. El unico momento en que el usuario
/// tiene el formulario delante y puede corregir es ahora.
/// </summary>
public sealed class ValidadorDeCredencial(ILogger<ValidadorDeCredencial> log)
{
    /// <summary>
    /// El mensaje es deliberadamente UNO SOLO para todos los fallos.
    ///
    /// Distinguir "ese usuario no existe" de "la clave es incorrecta" de "esa base no
    /// esta" convierte el formulario en un oraculo: con paciencia se enumeran usuarios
    /// y bases del servidor sin tener ninguna credencial valida. El usuario legitimo
    /// tiene los cuatro datos delante y los revisa; quien esta probando, no.
    /// </summary>
    public const string MensajeGenerico =
        "No se pudo conectar con esos datos. Revisá servidor, base, usuario y clave " +
        "tal como te los entregaron.";

    public async Task<(bool Ok, string? Error)> ValidarAsync(CredencialDatamart credencial, CancellationToken ct)
    {
        ConexionDatamartDirecta conexion;
        try
        {
            // Reusa la MISMA clase que arma la conexion en el conector local: si algun
            // dia cambia como se construye la cadena, cambia para los dos a la vez.
            conexion = new ConexionDatamartDirecta(
                credencial.Servidor, credencial.Base, credencial.Usuario, credencial.Clave,
                timeoutSegundos: 15);
        }
        catch (Exception ex)
        {
            // Faltan campos o la cadena no se puede armar. Eso si se puede decir: no
            // filtra nada del servidor, es un problema del formulario.
            return (false, ex.Message);
        }

        try
        {
            var cadena = await conexion.ResolverAsync(credencial.Base);
            await using var conn = new SqlConnection(cadena);
            await conn.OpenAsync(ct);

            // Abrir alcanza para saber que el login sirve y que la base existe.
            return (true, null);
        }
        catch (Exception ex)
        {
            // El detalle real va al log del servidor, donde lo puede leer soporte;
            // al usuario le llega el mensaje generico.
            log.LogWarning(ex, "Credencial rechazada para {descripcion}", credencial.Descripcion);
            return (false, MensajeGenerico);
        }
    }
}
