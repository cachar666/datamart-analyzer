using System.Text.Json;
using DatamartAnalyzer.Core.Models;
using DatamartAnalyzer.Core.Prebuilt;
using DatamartAnalyzer.Core.Rag;
using DatamartAnalyzer.Core.Schema;
using DatamartAnalyzer.Core.Sql;
using DatamartAnalyzer.Core.Store;

namespace DatamartAnalyzer.Mcp.Backends;

/// <summary>
/// Habla directo con SQL Server usando los cuatro datos que se le entregan al
/// usuario final: servidor, base, usuario y clave. No hay API en el medio.
///
/// La diferencia importante con el modo API esta en `preguntar`: aca NO hay una
/// llamada a un modelo. No hace falta — el conector corre dentro de Claude, asi que
/// la IA es quien esta leyendo esta respuesta. Lo que devuelve es el material para
/// que escriba el SQL: la consulta preconstruida si coincide, el contexto de negocio
/// del ERP relevante a la pregunta, y el esquema. Despues llama a ejecutar_sql.
/// </summary>
public sealed class BackendSql(
    ConexionDatamartDirecta conexion,
    ISqlExecutor executor,
    ISchemaService schema,
    IStoreRepository store,
    IStoreMigrator migrator,
    IPrebuiltQueriesService prebuilts,
    IDocumentService documento) : BackendBase
{
    public override string Modo => "sql";
    public override string Descripcion => $"SQL directo {conexion.Servidor}/{conexion.Base}";

    // ── Sesion ───────────────────────────────────────────────────────────────

    /// <summary>
    /// En este modo el datamart es UNO: el que corresponde a las credenciales que se
    /// cargaron al instalar el conector. Pedir otro no es un caso valido a medias,
    /// es un error — y decirlo claro evita que Claude insista contra una base que
    /// nunca va a poder abrir.
    /// </summary>
    public override Task<string> ResolverDatamartAsync(string? pedido = null)
    {
        var elegido = pedido ?? DatamartDeSesion;

        if (!string.IsNullOrWhiteSpace(elegido)
            && !elegido.Equals(conexion.Base, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Este conector esta configurado para el datamart '{conexion.Base}' en {conexion.Servidor} " +
                $"y no puede abrir '{elegido}'. Para trabajar con otro datamart hay que cambiar los datos " +
                "de conexion en la configuracion del conector (Servidor SQL, Base Datos, Usuario y Clave).");
        }

        return Task.FromResult(conexion.Base);
    }

    // ── Contexto ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Agrega la pista de autenticacion a un error, pero SOLO cuando el error ya
    /// ocurrio y tiene pinta de ser de conexion o de login.
    ///
    /// Antes esto se predecia: "sin usuario contra un servidor con dominio va a
    /// fallar". Era falso. Dejar Usuario y Clave vacios para entrar con la cuenta de
    /// Windows es el comportamiento buscado, y contra un servidor del dominio
    /// funciona perfecto. Predecir el fallo habria marcado como rota una instalacion
    /// sana. Reaccionar al fallo real nunca se equivoca.
    /// </summary>
    private string? PistaAutenticacion(string? error)
    {
        if (conexion.UsaAutenticacionSql || string.IsNullOrWhiteSpace(error)) return null;

        var esDeAcceso =
            error.Contains("Login failed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Error de inicio de sesion", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("inicio de sesi", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("no estaba accesible", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("not accessible", StringComparison.OrdinalIgnoreCase);

        if (!esDeAcceso) return null;

        return "Se esta usando autenticacion de Windows porque no se cargaron Usuario ni Clave. " +
               "Eso es correcto si el datamart acepta la cuenta de Windows de este equipo; " +
               "si en cambio te entregaron un usuario y una clave de SQL Server, cargalos en " +
               "Configuracion -> Extensiones -> Datamart ADPRO -> Configurar, y apaga y vuelve " +
               "a encender la extension.";
    }

    public override Task<JsonElement> ContextoAsync() => Task.FromResult(AJson(new
    {
        modo = "sql-directo",
        servidor = conexion.Servidor,
        usuario = conexion.Usuario,          // la clave NO se reporta nunca
        // Que quede explicito con que se autentico: sin esto, "no se cargo el
        // usuario" y "el usuario no existe" se ven igual desde afuera.
        autenticacion = conexion.UsaAutenticacionSql
            ? "SQL Server (usuario y clave)"
            : "Windows (integrada, sin usuario) — es lo esperado si Usuario y Clave se dejan vacios",
        datamarts = new[] { conexion.Base },
        mensaje = "Conexion directa: el conector trabaja contra un unico datamart, el de sus credenciales.",
    }));

    public override async Task<JsonElement> EstadoAlmacenAsync(string db)
    {
        var (ok, error) = await migrator.AsegurarInstaladoAsync(db);
        if (!ok)
        {
            var pista = PistaAutenticacion(error);
            return AJson(new StoreStatusDto(db, false, 0, 0, 0,
                $"No se pudo preparar el almacen: {error}" + (pista is null ? "" : $"\n{pista}")));
        }

        return AJson(await store.EstadoAsync(db));
    }

    public override async Task<JsonElement> EsquemaAsync(string db) =>
        AJson(await schema.ObtenerContextoCompletoAsync(db));

    public override async Task<JsonElement> ValoresFiltroAsync(string db, string tipo)
    {
        if (!FilterCatalog.Columnas.TryGetValue(tipo, out var columna))
            throw new InvalidOperationException(
                $"Filtro '{tipo}' desconocido. Disponibles: {FilterCatalog.TiposConocidos}.");

        var sql = $"SELECT DISTINCT [{columna}] FROM {FilterCatalog.TablaProyecto} " +
                  $"WHERE [{columna}] IS NOT NULL AND [{columna}] <> '' ORDER BY [{columna}]";

        // Sin filtros de sesion: listar los valores disponibles no debe estar
        // recortado por la seleccion actual, o no se podria ampliarla nunca.
        var r = await executor.EjecutarAsync(db, sql);
        if (!r.Exitoso) throw new InvalidOperationException(r.Error ?? "No se pudieron leer los valores del filtro.");

        var valores = (r.Datos ?? [])
            .Select(f => f.TryGetValue(columna, out var v) ? v?.ToString() : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        return AJson(new { tipo, valores, tabla = FilterCatalog.TablaProyecto, columna });
    }

    // ── Consultar ────────────────────────────────────────────────────────────

    public override async Task<JsonElement> PreguntarAsync(string db, string pregunta)
    {
        var prebuilt = prebuilts.Match(pregunta);

        if (prebuilt is not null)
        {
            var sqlConFiltros = FilterInjector.Inyectar(prebuilt.Sql, FiltrosONull);
            var datos = await executor.EjecutarAsync(db, sqlConFiltros);

            return AJson(new
            {
                origen = "consulta preconstruida",
                pregunta = prebuilt.Pregunta,
                sqlGenerado = sqlConFiltros,
                tipoRespuesta = prebuilt.TipoRespuesta,
                explicacion = prebuilt.ExplicacionTexto,
                grafico = prebuilt.Grafico,
                filtrosAplicados = FiltrosONull,
                resultado = datos,
            });
        }

        // Sin prebuilt: se devuelve el material, no una respuesta. Quien escribe el
        // SQL es Claude, que ya esta leyendo esto.
        var ctx = await schema.ObtenerContextoCompletoAsync(db);
        var seccionesErp = documento.ObtenerSeccionesRelevantes(pregunta, 6000);

        // Solo las tablas que la pregunta necesita. Mandar el esquema entero eran
        // ~56.000 caracteres que Claude tenia que leer antes de escribir una linea.
        var rel = SchemaRelevance.Seleccionar(ctx.Columnas, pregunta, seccionesErp);

        return AJson(new
        {
            origen = "sin consulta preconstruida",
            instruccion =
                "No hay una consulta preconstruida para esta pregunta. Escribi el SELECT usando el " +
                "contexto de negocio y el esquema que siguen, y ejecutalo con la herramienta ejecutar_sql. " +
                "Respeta los nombres de columna EXACTOS del esquema: no inventes columnas. " +
                "Solo se permite SELECT (o WITH para CTEs).",
            pregunta,
            contextoErp = seccionesErp,
            filtrosActivos = FiltrosONull,
            notaFiltros =
                "Los filtros activos se inyectan automaticamente al ejecutar; no los repitas en el WHERE. " +
                "Los filtros de proyecto asumen que la dimension [ADP_DTM_DIM].[Proyecto] se une con alias p.",
            esquema = new
            {
                database = ctx.Database,
                nota = rel.TablasOmitidas.Count == 0
                    ? "Esquema completo."
                    : "Esquema RECORTADO a las tablas relevantes para esta pregunta. " +
                      "Si necesitas alguna de las omitidas, pedila con describir_datamart.",
                tablasIncluidas = rel.TablasIncluidas,
                tablasOmitidas = rel.TablasOmitidas,
                columnas = rel.Columnas,
                vistas = ctx.Vistas,
            },
        });
    }

    public override async Task<JsonElement> EjecutarSqlAsync(string db, string sql, bool aplicarFiltros = true)
    {
        var filtros = aplicarFiltros ? FiltrosONull : null;
        var final = FilterInjector.Inyectar(sql, filtros);
        var r = await executor.EjecutarAsync(db, final);

        return AJson(new
        {
            sqlEjecutado = final,
            filtrosAplicados = filtros,
            exitoso = r.Exitoso,
            datos = r.Datos,
            totalFilas = r.TotalFilas,
            error = r.Error,
            pista = PistaAutenticacion(r.Error),
            tiempoEjecucionMs = r.TiempoEjecucionMs,
        });
    }

    // ── Almacen ──────────────────────────────────────────────────────────────

    /// <summary>
    /// El almacen se instala solo, al primer uso. Un usuario final no deberia tener
    /// que correr un script para poder guardar su primera consulta.
    /// </summary>
    private async Task AsegurarAlmacenAsync(string db)
    {
        var (ok, error) = await migrator.AsegurarInstaladoAsync(db);
        if (!ok)
        {
            var pista = PistaAutenticacion(error);
            throw new InvalidOperationException(
                $"El almacen de consultas y dashboards no esta disponible en '{db}': {error}" +
                (pista is null ? "" : $"\n{pista}"));
        }
    }

    public override async Task<JsonElement> ListarSugeridasAsync(string db, string? modulo)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.ListarSugeridasAsync(db, modulo));
    }

    public override async Task<JsonElement> CrearSugeridaAsync(
        string db, string pregunta, string categoria, string modulo)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.CrearSugeridaAsync(db,
            new CreateSuggestedQuestionRequest(pregunta, categoria, modulo)));
    }

    public override async Task BorrarSugeridaAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        if (!await store.BorrarSugeridaAsync(db, AGuid(id, "pregunta sugerida")))
            throw new InvalidOperationException($"No existe la pregunta sugerida {id} en {db}.");
    }

    public override async Task RegistrarUsoSugeridaAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        if (!await store.RegistrarUsoSugeridaAsync(db, AGuid(id, "pregunta sugerida")))
            throw new InvalidOperationException($"No existe la pregunta sugerida {id} en {db}.");
    }

    public override async Task<JsonElement> ListarCategoriasAsync(string db, string? modulo)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.ListarCategoriasAsync(db, modulo));
    }

    public override async Task<JsonElement> CrearCategoriaAsync(string db, string nombre, string modulo)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.CrearCategoriaAsync(db, new CreateSuggestedCategoryRequest(nombre, modulo)));
    }

    public override async Task BorrarCategoriaAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        if (!await store.BorrarCategoriaAsync(db, AGuid(id, "categoria")))
            throw new InvalidOperationException($"No existe la categoria {id} en {db}.");
    }

    public override async Task<JsonElement> ListarConsultasAsync(string db, string? buscar)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.ListarConsultasAsync(db, buscar));
    }

    public override async Task<JsonElement> ObtenerConsultaAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        var c = await store.ObtenerConsultaAsync(db, AGuid(id, "consulta"))
                ?? throw new InvalidOperationException($"No existe la consulta {id} en {db}.");
        return AJson(c);
    }

    public override async Task<JsonElement> GuardarConsultaAsync(
        string db, string pregunta, string sql, string tipoRespuesta, string? tags, GraficoPedido? grafico)
    {
        await AsegurarAlmacenAsync(db);
        var tipo = Enum.TryParse<TipoRespuesta>(tipoRespuesta, ignoreCase: true, out var t)
            ? t : TipoRespuesta.Tabla;

        var g = await PrepararGraficoAsync(db, sql, null, grafico);

        return AJson(await store.CrearConsultaAsync(db,
            new CreateSavedQueryRequest(pregunta, sql, tipo, g, tags)));
    }

    public override async Task BorrarConsultaAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        if (!await store.BorrarConsultaAsync(db, AGuid(id, "consulta")))
            throw new InvalidOperationException($"No existe la consulta {id} en {db}.");
    }

    public override async Task<JsonElement> ListarDashboardsAsync(string db)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.ListarDashboardsAsync(db));
    }

    public override async Task<JsonElement> VerDashboardAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        var d = await store.ObtenerDashboardAsync(db, AGuid(id, "dashboard"))
                ?? throw new InvalidOperationException($"No existe el dashboard {id} en {db}.");
        return AJson(d);
    }

    public override async Task<JsonElement> CrearDashboardAsync(string db, string nombre, string? descripcion)
    {
        await AsegurarAlmacenAsync(db);
        return AJson(await store.CrearDashboardAsync(db, new CreateDashboardRequest(nombre, descripcion)));
    }

    public override async Task<JsonElement> ActualizarDashboardAsync(
        string db, string id, string? nombre, string? descripcion)
    {
        await AsegurarAlmacenAsync(db);
        var d = await store.ActualizarDashboardAsync(db, AGuid(id, "dashboard"),
                    new UpdateDashboardRequest(nombre, descripcion))
                ?? throw new InvalidOperationException($"No existe el dashboard {id} en {db}.");
        return AJson(d);
    }

    public override async Task BorrarDashboardAsync(string db, string id)
    {
        await AsegurarAlmacenAsync(db);
        if (!await store.BorrarDashboardAsync(db, AGuid(id, "dashboard")))
            throw new InvalidOperationException($"No existe el dashboard {id} en {db}.");
    }

    public override async Task<JsonElement> AgregarPanelAsync(
        string db, string dashboardId, string titulo, string? consultaId, string? sql, string tipoRespuesta,
        GraficoPedido? grafico)
    {
        await AsegurarAlmacenAsync(db);

        var tipo = Enum.TryParse<TipoRespuesta>(tipoRespuesta, ignoreCase: true, out var t)
            ? t : TipoRespuesta.Tabla;

        Guid? consulta = string.IsNullOrWhiteSpace(consultaId) ? null : AGuid(consultaId, "consulta");

        // El grafico se valida contra las columnas reales antes de guardarlo; si no
        // hay grafico, el almacen se encarga de bajar el tipo a Tabla.
        var g = await PrepararGraficoAsync(db, sql, consultaId, grafico);

        var p = await store.AgregarPanelAsync(db, AGuid(dashboardId, "dashboard"),
                    new AddPanelRequest(titulo, consulta, sql, tipo, g))
                ?? throw new InvalidOperationException($"No existe el dashboard {dashboardId} en {db}.");

        return AJson(p);
    }

    public override async Task QuitarPanelAsync(string db, string dashboardId, string panelId)
    {
        await AsegurarAlmacenAsync(db);
        if (!await store.QuitarPanelAsync(db, AGuid(dashboardId, "dashboard"), AGuid(panelId, "panel")))
            throw new InvalidOperationException($"No existe el panel {panelId} en el dashboard {dashboardId}.");
    }
}
