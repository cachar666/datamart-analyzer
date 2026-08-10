# Conector Cowork ↔ Datamart Analyzer — Diseño

> Permitir que desde Cowork se elija la base de datos, se hagan preguntas al datamart, y se administren
> las consultas guardadas y los dashboards — compartiendo el mismo almacén que la aplicación web.
>
> Proyecto: `C:\Proyectos\AA\datamart-analyzer` · Diseño generado 2026-08-10

---

## 1. El obstáculo real

Hoy las consultas guardadas y los dashboards **viven en `localStorage` del navegador**:

```javascript
// frontend/src/App.jsx:51-61
const STORAGE_KEY    = 'datamart_saved_queries'
const DASHBOARDS_KEY = 'datamart_dashboards'
```

Eso significa que:

- No hay ningún endpoint que los exponga — el backend ni siquiera sabe que existen.
- Son por navegador y por perfil: si cambiás de equipo o limpiás el caché, se pierden.
- **Cowork no puede verlos ni administrarlos**, porque no hay nada que consultar.

Por lo tanto el conector no es lo primero que se construye. El orden obligado es:
**almacén compartido → endpoints → conector**. Sin ese almacén, un conector solo podría hacer preguntas
sueltas y nada de lo que pide el requerimiento sobre guardar y administrar.

Un segundo hallazgo, menor pero relevante para el diseño: ni las consultas guardadas ni los dashboards
guardan **a qué base de datos pertenecen**. Como el requerimiento arranca justamente por "definir a qué
base quiero trabajar", el modelo nuevo incorpora ese campo.

---

## 2. Arquitectura objetivo

```
        MÁQUINA DEL USUARIO (adp-005)                          NUBE
┌────────────────────────────────────────────┐        ┌──────────────────┐
│                                            │        │                  │
│  Claude Desktop ──stdio──► DatamartAnalyzer│        │   Cowork         │
│         ▲                  .Mcp (.NET)     │◄──────►│   (esta sesión)  │
│         │                       │          │ puente │                  │
│         │                       │ HTTP     │        └──────────────────┘
│         │                       ▼          │
│         │            localhost:5000/api    │
│         │                       │          │
│         │            DatamartAnalyzer.Api  │
│         │                  │         │     │
│  Navegador ──HTTP──────────┘         │     │
│  (React SPA)                         │     │
│                                      ▼     │
│                          SQL Server YOSEMITE
│                          ├── DatamartAnalyzer  ◄── almacén nuevo
│                          └── ADP_DTM_* (datamarts)
└────────────────────────────────────────────┘
```

**Por qué el servidor MCP corre en tu máquina y no en la nube:** esta sesión de Cowork vive en un
contenedor aislado de Anthropic que **no alcanza `localhost:5000` de tu PC** — ni con shell ni por red.
El patrón que ya usás funciona exactamente así: `helpdesk-write`, `sql-helpdesk` y `azure-devops` son
servidores MCP locales que Claude Desktop levanta en tu equipo y Cowork alcanza a través del puente de
dispositivo (por eso aparecen como `mcp__remote-devices__<servidor>__*`). El conector nuevo se suma a esa
lista con el mismo mecanismo: hereda tu autenticación Windows contra YOSEMITE sin exponer nada a internet.

---

## 3. Modelo de datos

Base nueva **`DatamartAnalyzer`** en YOSEMITE, separada de los datamarts. Cuatro tablas:

### `dbo.SavedQueries`

| Columna | Tipo | Notas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | `NEWSEQUENTIALID()` |
| `Database` | `nvarchar(128)` | **nuevo** — a qué datamart pertenece |
| `Pregunta` | `nvarchar(1000)` | texto en lenguaje natural |
| `Sql` | `nvarchar(max)` | SQL generado o editado |
| `TipoRespuesta` | `varchar(20)` | `Tabla` \| `Grafico` \| `Texto` \| `TablaMasGrafico` \| `TablaMasTexto` |
| `GraficoJson` | `nvarchar(max)` NULL | serialización de `ConfiguracionGrafico` |
| `Tags` | `nvarchar(400)` NULL | **nuevo** — etiquetas separadas por coma, para buscar desde Cowork |
| `CreatedAt` / `UpdatedAt` | `datetime2(0)` | |
| `CreatedBy` | `nvarchar(128)` | `SUSER_SNAME()` |

### `dbo.Dashboards`

| Columna | Tipo | Notas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `Nombre` | `nvarchar(200)` | único por `Database` |
| `Database` | `nvarchar(128)` | **nuevo** |
| `Descripcion` | `nvarchar(1000)` NULL | **nuevo** — para que Cowork sepa de qué trata |
| `CreatedAt` / `UpdatedAt` / `CreatedBy` | | |

### `dbo.DashboardPanels`

Refleja el objeto `panel` actual del frontend, más `Orden` para poder reordenar desde Cowork.

| Columna | Tipo | Notas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `DashboardId` | `uniqueidentifier` FK → `Dashboards` | `ON DELETE CASCADE` |
| `Titulo` | `nvarchar(200)` | |
| `Sql` / `SqlBase` | `nvarchar(max)` | |
| `EsPrebuilt` | `bit` | |
| `PrebuiltKey` | `nvarchar(400)` NULL | la pregunta clave del prebuilt |
| `FiltrosFijosJson` | `nvarchar(max)` NULL | |
| `AceptaFiltrosGlobales` | `bit` | default `1` |
| `TipoRespuesta` | `varchar(20)` | |
| `GraficoJson` | `nvarchar(max)` NULL | |
| `Orden` | `int` | **nuevo** — posición en la grilla |

### `dbo.FilterPresets`

Para "aplicar los filtros globales" desde Cowork sin repetir la selección cada vez.

| Columna | Tipo | Notas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `Nombre` | `nvarchar(200)` | ej. "Obras Norte activas" |
| `Database` | `nvarchar(128)` | |
| `FiltrosJson` | `nvarchar(max)` | `{"empresa":["NORTE"],"estado":["En ejecucion"]}` |
| `CreatedAt` / `CreatedBy` | | |

> **Sobre `CreatedBy` y multiusuario:** el backend se conecta con Integrated Security, así que todas las
> escrituras llegan con la identidad del proceso que corre la API, no la del usuario final. Hoy eso alcanza
> (sos vos en tu equipo). Si mañana esto se publica para varios usuarios hay que propagar la identidad del
> llamador — está anotado en riesgos (§7).

El script completo está en **`sql/01-create-store.sql`**, con guardas `IF NOT EXISTS` para poder correrlo
más de una vez sin romper nada.

---

## 4. Endpoints nuevos

Siguen la convención existente: `[ApiController]` + `[Route("api/[controller]")]`, records en `Models.cs`,
Dapper para los datos.

### `SavedQueriesController` — `/api/savedqueries`

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/api/savedqueries?database=&buscar=` | Lista con filtro opcional por BD y búsqueda en pregunta/tags |
| `GET` | `/api/savedqueries/{id}` | Una consulta |
| `POST` | `/api/savedqueries` | Crea |
| `PUT` | `/api/savedqueries/{id}` | Actualiza pregunta, SQL, tipo, gráfico o tags |
| `DELETE` | `/api/savedqueries/{id}` | Borra |
| `POST` | `/api/savedqueries/{id}/run` | Ejecuta con filtros opcionales → reusa `/api/analyze/execute` |

### `DashboardsController` — `/api/dashboards`

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/api/dashboards?database=` | Lista con conteo de paneles |
| `GET` | `/api/dashboards/{id}` | Dashboard con sus paneles ordenados |
| `POST` | `/api/dashboards` | Crea |
| `PUT` | `/api/dashboards/{id}` | Renombra / cambia descripción |
| `DELETE` | `/api/dashboards/{id}` | Borra en cascada |
| `POST` | `/api/dashboards/{id}/panels` | Agrega panel (desde SQL o desde una consulta guardada) |
| `PUT` | `/api/dashboards/{id}/panels/{panelId}` | Cambia título o `aceptaFiltrosGlobales` |
| `DELETE` | `/api/dashboards/{id}/panels/{panelId}` | Quita panel |
| `PUT` | `/api/dashboards/{id}/panels/order` | Reordena (lista de ids) |
| `POST` | `/api/dashboards/{id}/run` | Ejecuta todos los paneles con los filtros dados |

### `FilterPresetsController` — `/api/filterpresets`

`GET` (lista), `POST` (crea), `DELETE` — mismo patrón.

### `MigrationController` — `/api/migration/import`

`POST` único, recibe el volcado de `localStorage` y lo inserta con deduplicación por `sql`. Lo usa el
frontend una sola vez la primera vez que arranca contra el almacén nuevo.

---

## 5. Catálogo de herramientas MCP

Proyecto **`DatamartAnalyzer.Mcp`** dentro de `DatamartAnalyzer.sln`, paquete
[`ModelContextProtocol` 2.1.0](https://www.nuget.org/packages/ModelContextProtocol) (estable desde el 5 de
agosto de 2026, soporta net8.0+ — compatible con el `net9.0` del proyecto). Transporte stdio, tools
declaradas con `[McpServerToolType]` / `[McpServerTool]`, descubiertas con `WithToolsFromAssembly()`.

### Conexión y exploración

| Herramienta | Qué hace |
|---|---|
| `listar_bases` | Bases de datos datamart disponibles en YOSEMITE |
| `usar_base(database)` | Fija la BD de trabajo para el resto de la conversación |
| `base_actual` | Devuelve la BD y los filtros activos |
| `describir_datamart(database?)` | Hechos, dimensiones y vistas disponibles |

### Preguntar

| Herramienta | Qué hace |
|---|---|
| `preguntar(pregunta, database?, filtros?)` | El flujo completo: prebuilt o IA → SQL → datos. Devuelve tipo de respuesta, explicación, SQL y filas |
| `ejecutar_sql(database?, sql)` | Modo avanzado. Respeta la whitelist existente (solo `SELECT`/`WITH`, timeout 30s, tope de filas) |

### Consultas guardadas

| Herramienta | Qué hace |
|---|---|
| `listar_consultas(database?, buscar?)` | Lista y busca por texto o tags |
| `guardar_consulta(pregunta, sql, tipoRespuesta, grafico?, tags?)` | Guarda — típicamente encadenado tras `preguntar` |
| `actualizar_consulta(id, ...)` | Renombra, cambia SQL, tipo o tags |
| `borrar_consulta(id)` | Borra |
| `ejecutar_consulta_guardada(id, filtros?)` | Re-ejecuta con otros filtros |

### Dashboards

| Herramienta | Qué hace |
|---|---|
| `listar_dashboards(database?)` | Con conteo de paneles |
| `ver_dashboard(id)` | Detalle con paneles ordenados |
| `crear_dashboard(nombre, descripcion?)` | Crea |
| `borrar_dashboard(id)` | Borra en cascada |
| `agregar_panel(dashboardId, titulo, consultaGuardadaId? \| sql?, aceptaFiltrosGlobales?)` | Agrega desde una consulta guardada o desde SQL directo |
| `quitar_panel(dashboardId, panelId)` | Quita |
| `reordenar_paneles(dashboardId, panelIds[])` | Reordena |
| `configurar_panel(dashboardId, panelId, titulo?, aceptaFiltrosGlobales?)` | Ajusta |
| `ejecutar_dashboard(id, filtros?)` | Ejecuta todos los paneles y devuelve los resultados |

### Filtros globales

| Herramienta | Qué hace |
|---|---|
| `listar_valores_filtro(tipo, database?)` | Valores disponibles de empresa, macroproyecto, proyecto o estado |
| `fijar_filtros(filtros)` | Fija los filtros de la sesión (incluye insumo como texto libre) |
| `limpiar_filtros()` | Limpia |
| `guardar_preset_filtros(nombre)` / `aplicar_preset_filtros(nombre)` | Presets reutilizables |

**23 herramientas.** Es un catálogo grande; si en la práctica satura el contexto, la vía es consolidar las
familias CRUD en una sola tool con parámetro `accion` (`consultas(accion:"listar"|"guardar"|...)`), que baja
a unas 10. Prefiero arrancar explícito porque el modelo elige mejor con nombres claros, y consolidar solo si
hace falta.

**Estado de sesión:** el servidor MCP corre como proceso por cliente, así que `usar_base` y `fijar_filtros`
guardan estado en memoria del proceso. Se pierde al reiniciar Claude Desktop — por eso existen los presets,
que sí persisten.

---

## 6. Plan por fases

| Fase | Entregable | Archivos | Esfuerzo |
|---|---|---|---|
| **1** | **Almacén + endpoints** — script SQL, records, repositorios Dapper, tres controladores nuevos | `sql/01-create-store.sql`, `Models/Models.cs`, `Services/StoreRepository.cs`, `Controllers/StoreControllers.cs` | ~1 día |
| **2** | **Frontend migra** — `api.js` y `App.jsx` contra la API, importación automática de lo que hay en `localStorage` | `frontend/src/services/api.js`, `frontend/src/App.jsx` | ~1 día |
| **3** | **Servidor MCP** — proyecto nuevo, 23 tools, registro en Claude Desktop | `backend/DatamartAnalyzer.Mcp/` | ~1½ días |
| **4** | **Cierre** — presets de filtros en la UI, tests de los repositorios, `TECHNICAL.md` actualizado | varios | ~½ día |

Cada fase deja el sistema funcionando: al terminar la 1 la app sigue andando igual (el frontend todavía usa
`localStorage`); al terminar la 2 ya compartís almacén entre navegadores; recién en la 3 aparece Cowork.

---

## 7. Riesgos y decisiones abiertas

**La API no tiene autenticación.** Hoy no importa porque solo escucha en localhost y solo lee. Al agregar
`POST`/`PUT`/`DELETE` que escriben en SQL Server, cualquier proceso de tu máquina puede modificar tus
dashboards. Aceptable para una herramienta local; **si esto alguna vez se publica en un servidor, necesita
autenticación antes**, no después.

**`ejecutar_sql` desde Cowork.** La tool reusa la whitelist del backend (`SELECT`/`WITH` únicamente, lista
negra de DML/DDL, timeout 30s, tope de filas). No se agrega ningún camino que la esquive — es importante que
siga siendo así.

**Identidad del usuario.** Ver la nota de `CreatedBy` en §3. Mientras seas el único usuario no es problema.

**`localStorage` como fuente única durante la transición.** Entre la fase 1 y la 2 conviven los dos
almacenes. La importación de la fase 2 deduplica por `sql`, así que correrla dos veces no duplica registros
— pero si en ese intervalo guardás cosas nuevas en el navegador **y** desde otro lado, hay que decidir cuál
gana. Propuesta: la importación solo agrega lo que no existe, nunca pisa.

**Prebuilts y paneles.** Un panel prebuilt guarda `prebuiltKey` (la pregunta exacta) y el SQL vive
server-side en `PrebuiltQueriesService`. Si esa pregunta cambia de texto, el panel deja de resolver. Ya pasa
hoy; el modelo nuevo lo hereda. Vale un chequeo de integridad en la fase 4.

**Reinicio del backend.** El `DocumentService` sigue siendo singleton: cualquier cambio en
`docs/contexto-erp.md` necesita reiniciar la API. No bloquea este proyecto, pero la recarga en caliente
(pendiente #7 del reporte del RAG) se vuelve más molesta cuando hay más gente usando el sistema.

---

## 8. Cómo se verá en uso

```
Vos:    ¿qué bases tengo disponibles?
Claude: [listar_bases] → ADP_DTM_CONSTRUCTORA, ADP_DTM_NORTE, ...

Vos:    trabajemos con ADP_DTM_NORTE, solo obras en ejecución
Claude: [usar_base] [fijar_filtros {estado:["En ejecucion"]}]

Vos:    ¿cuál es el costo ejecutado vs presupuestado por proyecto?
Claude: [preguntar] → tabla + gráfico de barras, 14 proyectos

Vos:    guardala y ponela en el dashboard de gerencia
Claude: [guardar_consulta] [listar_dashboards] [agregar_panel]
        → "Guardada y agregada como panel 4 de 'Gerencia'"

Vos:    mostrame cómo quedó el dashboard
Claude: [ejecutar_dashboard] → los 4 paneles con datos
```

---

## Fuentes

- [NuGet — ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)
- [MCP C# SDK — Getting Started](https://csharp.sdk.modelcontextprotocol.io/v1/concepts/getting-started.html)
- [modelcontextprotocol/csharp-sdk en GitHub](https://github.com/modelcontextprotocol/csharp-sdk)
