using System.ComponentModel;
using System.Text.Json;
using DatamartAnalyzer.Mcp.Backends;
using ModelContextProtocol.Server;

namespace DatamartAnalyzer.Mcp;

/// <summary>
/// Herramientas expuestas a Claude. Ninguna sabe si detras hay una API o una
/// conexion directa a SQL Server: eso lo decide IBackend al arrancar.
///
/// Todas pasan por Seguro(): si algo falla, la respuesta trae el motivo en texto
/// claro. Dejar escapar la excepcion hace que el SDK devuelva "An error occurred
/// invoking 'X'", que no le sirve ni a Claude ni al usuario para saber que hacer.
/// </summary>
[McpServerToolType]
public class DatamartTools(IBackend backend)
{
    private static readonly JsonSerializerOptions Salida = new() { WriteIndented = true };

    private static string Json(object? v) => JsonSerializer.Serialize(v, Salida);

    // Descripciones de los parametros de grafico. Son const y compartidas porque
    // guardar_consulta y agregar_panel tienen que pedir exactamente lo mismo: si una
    // describe los ejes de una forma y la otra de otra, el grafico sale bien por un
    // camino y en blanco por el otro.
    private const string DescTipoGrafico =
        "Tipo de gráfico: Barras, Lineas, Torta, Area o Dispersion. " +
        "Obligatorio junto con los ejes si tipoRespuesta incluye gráfico.";

    private const string DescEjeX =
        "Columna del eje X (la categoría: proyecto, mes, estado...). " +
        "Tiene que ser el nombre EXACTO de una columna del resultado del SQL.";

    private const string DescEjeY =
        "Columna del eje Y (el valor a graficar). Tiene que ser el nombre EXACTO de una " +
        "columna NUMÉRICA del resultado del SQL — si es texto, el gráfico sale plano en cero.";

    /// <summary>
    /// Ejecuta la operacion y convierte cualquier fallo en una respuesta legible.
    /// La causa raiz suele estar mas abajo (una SqlException envuelta, por ejemplo)
    /// y es la unica linea que dice algo util.
    /// </summary>
    private async Task<string> SeguroCtx(Func<Task<object?>> operacion)
    {
        try
        {
            return Json(await operacion());
        }
        catch (Exception ex)
        {
            var raiz = ex;
            while (raiz.InnerException is not null) raiz = raiz.InnerException;

            return Json(new
            {
                error = ex.Message,
                detalle = ReferenceEquals(raiz, ex) ? null : raiz.Message,
                conexion = backend.Descripcion,
            });
        }
    }

    // ─── Contexto ────────────────────────────────────────────────────────────

    [McpServerTool(Name = "estado")]
    [Description("Estado de la conexión: contra qué está trabajando el conector (API o SQL directo), datamart activo, si el almacén de consultas y dashboards está instalado, y qué filtros hay puestos. Usar al empezar.")]
    public async Task<string> Estado()
    {
        object? contexto, almacen = null;
        string? db = null;

        try { contexto = await backend.ContextoAsync(); }
        catch (Exception ex) { contexto = new { error = ex.Message }; }

        try
        {
            db = await backend.ResolverDatamartAsync();
            almacen = await backend.EstadoAlmacenAsync(db);
        }
        catch (Exception ex) { almacen = new { error = ex.Message }; }

        return Json(new
        {
            modo = backend.Modo,
            conexion = backend.Descripcion,
            datamartActivo = db,
            contexto,
            almacen,
            filtrosActivos = backend.Filtros,
        });
    }

    [McpServerTool(Name = "listar_datamarts")]
    [Description("Datamarts disponibles. En modo ERP devuelve el que declara dbo.ADPConfig; con conexión directa, el único que corresponde a las credenciales del conector.")]
    public Task<string> ListarDatamarts() =>
        SeguroCtx(async () => await backend.ContextoAsync());

    [McpServerTool(Name = "usar_datamart")]
    [Description("Fija el datamart sobre el que trabajan el resto de las herramientas durante esta conversación.")]
    public Task<string> UsarDatamart(
        [Description("Nombre de la base del datamart, por ejemplo SincoEthussTCoindustrialDW.")] string datamart) =>
        SeguroCtx(async () =>
        {
            // Se resuelve ANTES de fijar: si el backend no puede con esa base, que
            // falle acá con un mensaje claro y no en la siguiente herramienta.
            var db = await backend.ResolverDatamartAsync(datamart);
            backend.FijarDatamart(db);
            return new { datamartActivo = db, almacen = await backend.EstadoAlmacenAsync(db) };
        });

    [McpServerTool(Name = "describir_datamart")]
    [Description("Estructura del datamart: tablas de hechos y dimensiones con sus campos, y las vistas disponibles. Útil antes de escribir SQL a mano.")]
    public Task<string> DescribirDatamart(
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () => await backend.EsquemaAsync(await backend.ResolverDatamartAsync(datamart)));

    // ─── Preguntar ───────────────────────────────────────────────────────────

    [McpServerTool(Name = "preguntar")]
    [Description("Hace una pregunta en lenguaje natural sobre el datamart. Si hay una consulta preconstruida que coincide, la ejecuta y devuelve los datos. Si no, devuelve el contexto de negocio del ERP y el esquema para que escribas el SELECT vos mismo y lo corras con ejecutar_sql.")]
    public Task<string> Preguntar(
        [Description("La pregunta, en español. Ej: ¿Cuál es el costo ejecutado vs presupuestado por proyecto?")] string pregunta,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
            await backend.PreguntarAsync(await backend.ResolverDatamartAsync(datamart), pregunta));

    [McpServerTool(Name = "ejecutar_sql")]
    [Description("Ejecuta un SELECT contra el datamart y aplica los filtros globales activos. Solo lectura: se rechaza cualquier cosa que no sea SELECT o WITH, y se acotan filas y tiempo.")]
    public Task<string> EjecutarSql(
        [Description("Consulta SQL. Debe empezar con SELECT o WITH.")] string sql,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
            await backend.EjecutarSqlAsync(await backend.ResolverDatamartAsync(datamart), sql));

    // ─── Preguntas sugeridas ─────────────────────────────────────────────────

    [McpServerTool(Name = "listar_preguntas_sugeridas")]
    [Description("Catálogo de preguntas que este datamart sabe responder, agrupadas por categoría, con cuántas veces las usó el equipo y cuántas el usuario actual. Es lo primero que conviene mirar cuando alguien pregunta qué se le puede preguntar al datamart, o cuando una pregunta suya se parece a una del catálogo.")]
    public Task<string> ListarPreguntasSugeridas(
        [Description("ADPRO o SRM. Si se omite, devuelve las de los dos módulos.")] string? modulo = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
            await backend.ListarSugeridasAsync(await backend.ResolverDatamartAsync(datamart), modulo));

    [McpServerTool(Name = "agregar_pregunta_sugerida")]
    [Description("USAR SOLO SI EL USUARIO LO PIDE EXPLÍCITAMENTE (\"agregá esta pregunta al catálogo\", \"guardá esta pregunta como sugerida\"). NO llamar por iniciativa propia porque una pregunta parezca útil: el catálogo es curado y se llenaría de ruido. Agrega la pregunta al datamart, visible también en la aplicación web. Si la categoría no existe, se crea como categoría del usuario.")]
    public Task<string> AgregarPreguntaSugerida(
        [Description("La pregunta, redactada como se la haría alguien del negocio.")] string pregunta,
        [Description("Categoría bajo la que agruparla. Si no existe, se crea. Ej: Costos, Materiales, Tesorería.")] string categoria = "Mis preguntas",
        [Description("ADPRO o SRM.")] string modulo = "ADPRO",
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () => await backend.CrearSugeridaAsync(
            await backend.ResolverDatamartAsync(datamart), pregunta, categoria, modulo));

    [McpServerTool(Name = "borrar_pregunta_sugerida")]
    [Description("Borra una pregunta del catálogo. SOLO se pueden borrar las que agregó el usuario (origen 'Usuario'); las del sistema no se borran, se ocultan. USAR SOLO SI EL USUARIO LO PIDE.")]
    public Task<string> BorrarPreguntaSugerida(
        [Description("Id de la pregunta sugerida a borrar.")] string id,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            await backend.BorrarSugeridaAsync(await backend.ResolverDatamartAsync(datamart), id);
            return new { borrada = id };
        });

    [McpServerTool(Name = "listar_categorias_sugeridas")]
    [Description("Categorías del catálogo, con su origen (Sistema o Usuario) y cuántas preguntas tiene cada una. Útil antes de agregar una pregunta, para reusar una categoría existente en vez de inventar una parecida.")]
    public Task<string> ListarCategoriasSugeridas(
        [Description("ADPRO o SRM. Si se omite, devuelve las de los dos módulos.")] string? modulo = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
            await backend.ListarCategoriasAsync(await backend.ResolverDatamartAsync(datamart), modulo));

    [McpServerTool(Name = "crear_categoria_sugerida")]
    [Description("Crea una categoría propia en el catálogo. USAR SOLO SI EL USUARIO LO PIDE. Normalmente no hace falta: al agregar una pregunta con una categoría que no existe, se crea sola.")]
    public Task<string> CrearCategoriaSugerida(
        [Description("Nombre de la categoría. Máximo 50 caracteres.")] string nombre,
        [Description("ADPRO o SRM.")] string modulo = "ADPRO",
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () => await backend.CrearCategoriaAsync(
            await backend.ResolverDatamartAsync(datamart), nombre, modulo));

    [McpServerTool(Name = "borrar_categoria_sugerida")]
    [Description("Borra una categoría del catálogo. SOLO las que creó el usuario, y solo si ya no tiene preguntas adentro. Las categorías del sistema no se borran. USAR SOLO SI EL USUARIO LO PIDE.")]
    public Task<string> BorrarCategoriaSugerida(
        [Description("Id de la categoría a borrar.")] string id,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            await backend.BorrarCategoriaAsync(await backend.ResolverDatamartAsync(datamart), id);
            return new { borrada = id };
        });

    [McpServerTool(Name = "marcar_uso_pregunta_sugerida")]
    [Description("Deja constancia de que se usó una pregunta del catálogo. Sirve para que el Top refleje lo que el equipo realmente consulta. Llamarla después de responder una pregunta que salió del catálogo.")]
    public Task<string> MarcarUsoPreguntaSugerida(
        [Description("Id de la pregunta sugerida.")] string id,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            await backend.RegistrarUsoSugeridaAsync(await backend.ResolverDatamartAsync(datamart), id);
            return new { registrada = id };
        });

    // ─── Consultas guardadas ─────────────────────────────────────────────────

    [McpServerTool(Name = "listar_consultas")]
    [Description("Consultas guardadas del datamart, con su SQL. Acepta un texto para buscar en la pregunta o en las etiquetas.")]
    public Task<string> ListarConsultas(
        [Description("Texto a buscar. Opcional.")] string? buscar = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
            await backend.ListarConsultasAsync(await backend.ResolverDatamartAsync(datamart), buscar));

    [McpServerTool(Name = "guardar_consulta")]
    [Description("USAR SOLO SI EL USUARIO LO PIDE EXPLÍCITAMENTE (\"guardá esta consulta\", \"dejala como favorita\"). NO guardar por iniciativa propia cada consulta que se ejecuta: el listado es del usuario y se llenaría de ruido. Guarda la consulta como favorita en el datamart, visible también en la aplicación web.")]
    public Task<string> GuardarConsulta(
        [Description("La pregunta en lenguaje natural.")] string pregunta,
        [Description("El SQL a guardar.")] string sql,
        [Description("Tabla, Grafico, Texto, TablaMasGrafico o TablaMasTexto.")] string tipoRespuesta = "Tabla",
        [Description("Etiquetas separadas por coma, para buscarla después.")] string? tags = null,
        [Description(DescTipoGrafico)] string? graficoTipo = null,
        [Description(DescEjeX)] string? graficoEjeX = null,
        [Description(DescEjeY)] string? graficoEjeY = null,
        [Description("Columna por la que separar en series. Opcional.")] string? graficoAgrupacion = null,
        [Description("Título del gráfico. Opcional.")] string? graficoTitulo = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () => await backend.GuardarConsultaAsync(
            await backend.ResolverDatamartAsync(datamart), pregunta, sql, tipoRespuesta, tags,
            new GraficoPedido(graficoTipo, graficoEjeX, graficoEjeY, graficoAgrupacion, graficoTitulo, null)));

    [McpServerTool(Name = "borrar_consulta")]
    [Description("Borra una consulta guardada por su id.")]
    public Task<string> BorrarConsulta(
        [Description("Id de la consulta.")] string id,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            await backend.BorrarConsultaAsync(await backend.ResolverDatamartAsync(datamart), id);
            return new { borrada = id };
        });

    [McpServerTool(Name = "ejecutar_consulta")]
    [Description("Ejecuta una consulta guardada y devuelve sus datos, aplicando los filtros globales activos.")]
    public Task<string> EjecutarConsulta(
        [Description("Id de la consulta guardada.")] string id,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var db = await backend.ResolverDatamartAsync(datamart);
            var c = await backend.ObtenerConsultaAsync(db, id);
            var sql = c.GetProperty("sql").GetString()!;
            return new { consulta = c, resultado = await backend.EjecutarSqlAsync(db, sql) };
        });

    // ─── Dashboards ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "listar_dashboards")]
    [Description("Dashboards guardados en el datamart, con la cantidad de paneles de cada uno.")]
    public Task<string> ListarDashboards(
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () => await backend.ListarDashboardsAsync(await backend.ResolverDatamartAsync(datamart)));

    [McpServerTool(Name = "ver_dashboard")]
    [Description("Detalle de un dashboard con todos sus paneles, incluido el SQL de cada uno. No ejecuta nada.")]
    public Task<string> VerDashboard(
        [Description("Id o nombre del dashboard.")] string dashboard,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var db = await backend.ResolverDatamartAsync(datamart);
            return await backend.VerDashboardAsync(db, await ResolverDashboardIdAsync(db, dashboard));
        });

    [McpServerTool(Name = "crear_dashboard")]
    [Description("Crea un dashboard vacío en el datamart.")]
    public Task<string> CrearDashboard(
        [Description("Nombre del dashboard. Debe ser único dentro del datamart.")] string nombre,
        [Description("Descripción de qué muestra. Opcional pero recomendada.")] string? descripcion = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () => await backend.CrearDashboardAsync(
            await backend.ResolverDatamartAsync(datamart), nombre, descripcion));

    [McpServerTool(Name = "borrar_dashboard")]
    [Description("Borra un dashboard y todos sus paneles.")]
    public Task<string> BorrarDashboard(
        [Description("Id o nombre del dashboard.")] string dashboard,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var db = await backend.ResolverDatamartAsync(datamart);
            var id = await ResolverDashboardIdAsync(db, dashboard);
            await backend.BorrarDashboardAsync(db, id);
            return new { borrado = id };
        });

    [McpServerTool(Name = "renombrar_dashboard")]
    [Description("Cambia el nombre o la descripción de un dashboard.")]
    public Task<string> RenombrarDashboard(
        [Description("Id o nombre actual del dashboard.")] string dashboard,
        [Description("Nombre nuevo. Opcional.")] string? nombre = null,
        [Description("Descripción nueva. Opcional.")] string? descripcion = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var db = await backend.ResolverDatamartAsync(datamart);
            var id = await ResolverDashboardIdAsync(db, dashboard);
            return await backend.ActualizarDashboardAsync(db, id, nombre, descripcion);
        });

    [McpServerTool(Name = "agregar_panel")]
    [Description("Agrega un panel a un dashboard, desde una consulta guardada o desde SQL directo. Hay que dar exactamente uno de los dos. " +
                 "IMPORTANTE: si tipoRespuesta es Grafico o TablaMasGrafico, hay que mandar TAMBIÉN graficoEjeX y graficoEjeY con nombres de columna EXACTOS " +
                 "del resultado del SQL. Sin eso el panel se guarda como tabla, porque un panel que dice que dibuja y no dibuja aparece vacío en la aplicación. " +
                 "Si no estás seguro de cómo se llaman las columnas, ejecutá el SQL primero con ejecutar_sql y mirá el resultado.")]
    public Task<string> AgregarPanel(
        [Description("Id o nombre del dashboard.")] string dashboard,
        [Description("Título del panel.")] string titulo,
        [Description("Id de una consulta guardada. Excluyente con sql.")] string? consultaId = null,
        [Description("SQL directo. Excluyente con consultaId.")] string? sql = null,
        [Description("Tabla, Grafico, Texto, TablaMasGrafico o TablaMasTexto.")] string tipoRespuesta = "Tabla",
        [Description(DescTipoGrafico)] string? graficoTipo = null,
        [Description(DescEjeX)] string? graficoEjeX = null,
        [Description(DescEjeY)] string? graficoEjeY = null,
        [Description("Columna por la que separar en series. Opcional.")] string? graficoAgrupacion = null,
        [Description("Título del gráfico. Opcional; si se omite se usa el del panel.")] string? graficoTitulo = null,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var tieneConsulta = !string.IsNullOrWhiteSpace(consultaId);
            var tieneSql = !string.IsNullOrWhiteSpace(sql);
            if (tieneConsulta == tieneSql)
                throw new InvalidOperationException("Indicá consultaId o sql — exactamente uno de los dos.");

            var db = await backend.ResolverDatamartAsync(datamart);
            var id = await ResolverDashboardIdAsync(db, dashboard);

            var grafico = new GraficoPedido(graficoTipo, graficoEjeX, graficoEjeY,
                                            graficoAgrupacion, graficoTitulo ?? titulo, null);

            return await backend.AgregarPanelAsync(db, id, titulo, consultaId, sql, tipoRespuesta, grafico);
        });

    [McpServerTool(Name = "quitar_panel")]
    [Description("Quita un panel de un dashboard.")]
    public Task<string> QuitarPanel(
        [Description("Id o nombre del dashboard.")] string dashboard,
        [Description("Id del panel.")] string panelId,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var db = await backend.ResolverDatamartAsync(datamart);
            var id = await ResolverDashboardIdAsync(db, dashboard);
            await backend.QuitarPanelAsync(db, id, panelId);
            return new { quitado = panelId };
        });

    [McpServerTool(Name = "ejecutar_dashboard")]
    [Description("Ejecuta todos los paneles de un dashboard y devuelve sus datos, aplicando los filtros globales activos. Es lo que hay que llamar antes de dibujar un dashboard como artefacto.")]
    public Task<string> EjecutarDashboard(
        [Description("Id o nombre del dashboard.")] string dashboard,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
        {
            var db = await backend.ResolverDatamartAsync(datamart);
            var id = await ResolverDashboardIdAsync(db, dashboard);
            var det = await backend.VerDashboardAsync(db, id);

            var resultados = new List<object>();
            foreach (var p in det.GetProperty("paneles").EnumerateArray())
            {
                var titulo = p.GetProperty("titulo").GetString();
                // Un panel puede declarar que ignora los filtros globales.
                var acepta = !p.TryGetProperty("aceptaFiltrosGlobales", out var a) || a.GetBoolean();

                try
                {
                    var datos = await backend.EjecutarSqlAsync(db, p.GetProperty("sql").GetString()!, acepta);

                    resultados.Add(new
                    {
                        panelId = p.GetProperty("id").GetString(),
                        titulo,
                        tipoRespuesta = p.TryGetProperty("tipoRespuesta", out var t) ? t.GetString() : "Tabla",
                        grafico = p.TryGetProperty("grafico", out var g) ? (object?)g : null,
                        aceptaFiltrosGlobales = acepta,
                        resultado = datos,
                    });
                }
                catch (Exception ex)
                {
                    // Un panel roto no debe tumbar el dashboard entero.
                    resultados.Add(new { titulo, error = ex.Message });
                }
            }

            return new
            {
                dashboard = det.GetProperty("nombre").GetString(),
                database = db,
                filtrosAplicados = backend.Filtros,
                paneles = resultados,
            };
        });

    // ─── Filtros globales ────────────────────────────────────────────────────

    [McpServerTool(Name = "listar_valores_filtro")]
    [Description("Valores disponibles para un filtro: empresa, macroproyecto, proyecto o estado.")]
    public Task<string> ListarValoresFiltro(
        [Description("empresa, macroproyecto, proyecto o estado.")] string tipo,
        [Description("Datamart. Si se omite, usa el activo.")] string? datamart = null) =>
        SeguroCtx(async () =>
            await backend.ValoresFiltroAsync(await backend.ResolverDatamartAsync(datamart), tipo));

    [McpServerTool(Name = "fijar_filtros")]
    [Description("Fija los filtros globales de la conversación. Se aplican a preguntar, ejecutar_sql, ejecutar_consulta y ejecutar_dashboard. Ejemplo: empresa=NORTE, estado=En ejecucion.")]
    public Task<string> FijarFiltros(
        [Description("JSON con los filtros. Ej: {\"empresa\":[\"NORTE\"],\"estado\":[\"En ejecucion\"]}")] string filtrosJson) =>
        SeguroCtx(() =>
        {
            var filtros = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(filtrosJson)
                          ?? throw new InvalidOperationException("No pude interpretar el JSON de filtros.");
            backend.FijarFiltros(filtros);
            return Task.FromResult<object?>(new { filtrosActivos = backend.Filtros });
        });

    [McpServerTool(Name = "limpiar_filtros")]
    [Description("Quita todos los filtros globales de la conversación.")]
    public Task<string> LimpiarFiltros() =>
        SeguroCtx(() =>
        {
            backend.LimpiarFiltros();
            return Task.FromResult<object?>(new { filtrosActivos = backend.Filtros });
        });

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Acepta id o nombre. Que se pueda decir "el dashboard de Gerencia" en vez de
    /// un GUID es la diferencia entre una herramienta usable y una que no lo es.
    /// </summary>
    private async Task<string> ResolverDashboardIdAsync(string db, string dashboard)
    {
        if (Guid.TryParse(dashboard, out _)) return dashboard;

        var lista = await backend.ListarDashboardsAsync(db);
        foreach (var d in lista.EnumerateArray())
        {
            if (string.Equals(d.GetProperty("nombre").GetString(), dashboard, StringComparison.OrdinalIgnoreCase))
                return d.GetProperty("id").GetString()!;
        }

        var nombres = string.Join(", ", lista.EnumerateArray()
            .Select(d => d.GetProperty("nombre").GetString()));

        throw new InvalidOperationException(
            $"No encontré el dashboard '{dashboard}' en {db}. Disponibles: {(string.IsNullOrEmpty(nombres) ? "ninguno" : nombres)}");
    }
}
