# Datamart Analyzer — Documentacion Tecnica

**Version**: 1.2.0
**Fecha**: Marzo 2026
**Estado**: Operativo en desarrollo

---

## Tabla de Contenidos

1. [Vision General](#1-vision-general)
2. [Arquitectura del Sistema](#2-arquitectura-del-sistema)
3. [Stack Tecnologico y Versiones](#3-stack-tecnologico-y-versiones)
4. [Estructura del Proyecto](#4-estructura-del-proyecto)
5. [Backend — ASP.NET Core API](#5-backend--aspnet-core-api)
6. [Frontend — React SPA](#6-frontend--react-spa)
7. [Integracion con IA (Anthropic Claude)](#7-integracion-con-ia-anthropic-claude)
8. [Base de Datos y Metadatos](#8-base-de-datos-y-metadatos)
9. [Reglas Criticas del Datamart ADPRO](#9-reglas-criticas-del-datamart-adpro)
10. [Configuracion](#10-configuracion)
11. [Seguridad](#11-seguridad)
12. [Rendimiento y Caching](#12-rendimiento-y-caching)
13. [Flujo Completo de una Consulta](#13-flujo-completo-de-una-consulta)
14. [API Endpoints](#14-api-endpoints)
15. [Costos de la IA](#15-costos-de-la-ia)
16. [Despliegue](#16-despliegue)
17. [Troubleshooting](#17-troubleshooting)
18. [Alcances del Proyecto](#18-alcances-del-proyecto)
19. [Mejoras Futuras](#19-mejoras-futuras)

---

## 1. Vision General

**Datamart Analyzer** es una aplicacion fullstack que permite a usuarios de negocio consultar datamarts del ERP ADPRO/SincoERP usando lenguaje natural, sin necesidad de conocer SQL.

El sistema:
- Se conecta al servidor SQL Server **YOSEMITE** mediante autenticacion de Windows
- Lee la estructura de hechos y dimensiones desde la vista `[ADP_DTM_VCONF].[DefinicionesCamposTabla]`
- Usa Anthropic Claude para traducir preguntas en espanol a consultas SQL
- Ejecuta las consultas y presenta los resultados como tablas interactivas, graficos o texto
- Incluye consultas **preconstruidas** para preguntas frecuentes de ADPRO y SRM (sin costo de IA)
- Permite modo **SQL Directo** para ejecutar queries manuales sin pasar por la IA

El sistema esta orientado a **analisis de costos y consumos de proyectos de construccion** dentro del ERP ADPRO, y a **gestion de proveedores y licitaciones** del modulo SRM.

---

## 2. Arquitectura del Sistema

```
                    ┌─────────────────────────────────┐
                    │         Usuario Final             │
                    │   (Navegador Web - cualquier)    │
                    └────────────────┬────────────────┘
                                     │ HTTP
                    ┌────────────────▼────────────────┐
                    │       Frontend (React SPA)        │
                    │    http://localhost:5173          │
                    │   Vite + Tailwind + Recharts     │
                    └────────────────┬────────────────┘
                                     │ HTTP/REST (CORS)
                    ┌────────────────▼────────────────┐
                    │    Backend (ASP.NET Core 9)       │
                    │    http://localhost:5000          │
                    │  Controllers → Services → Data   │
                    └──────────┬──────────┬───────────┘
                               │          │
              ┌────────────────▼──┐   ┌───▼──────────────────┐
              │  SQL Server        │   │  Anthropic Claude API  │
              │  YOSEMITE          │   │  api.anthropic.com     │
              │  Windows Auth      │   │  claude-haiku-4-5-...  │
              └───────────────────┘   └────────────────────────┘
```

**Patron de comunicacion**:
- Frontend ↔ Backend: REST/JSON sobre HTTP
- Backend ↔ SQL Server: Windows Authentication (sin usuario/password)
- Backend ↔ Claude API: HTTPS con API Key en header `x-api-key`

---

## 3. Stack Tecnologico y Versiones

### Backend

| Componente | Version | Uso |
|---|---|---|
| .NET / ASP.NET Core | 9.0 | Framework web y runtime |
| Dapper | 2.1.35 | Micro-ORM para queries SQL |
| Microsoft.Data.SqlClient | 5.2.2 | Driver SQL Server con Windows Auth |
| Swashbuckle.AspNetCore | 6.6.2 | Swagger/OpenAPI UI |
| System.Text.Json | 8.0.5 | Serializacion JSON |
| Anthropic.SDK | 3.7.3 (resuelto 4.0.0) | Referencia de tipos (API via HttpClient directo) |

> **Nota**: `DocumentFormat.OpenXml` fue eliminado. El RAG ahora lee un archivo `.md` en vez de `.docx`.

### Frontend

| Componente | Version | Uso |
|---|---|---|
| React | 18.3.1 | Framework UI |
| Vite | 5.4.11 | Build tool y dev server |
| Tailwind CSS | 3.4.17 | Estilos utilitarios |
| Recharts | 2.13.3 | Graficos (Barras, Lineas, Torta, Area) |
| Axios | 1.7.9 | Cliente HTTP al backend |
| lucide-react | 0.263.1 | Iconos |

---

## 4. Estructura del Proyecto

```
datamart-analyzer/
├── backend/
│   └── DatamartAnalyzer.Api/
│       ├── Controllers/
│       │   └── Controllers.cs          # Todos los controllers en un archivo
│       ├── Models/
│       │   └── Models.cs               # Records y enums de dominio (incl. UsageInfo)
│       ├── Services/
│       │   ├── AnthropicService.cs     # Integracion con Claude API + extraccion de UsageInfo
│       │   ├── DocumentService.cs      # Carga del RAG desde contexto-erp.md
│       │   ├── PrebuiltQueriesService.cs # Consultas preconstruidas ADPRO y SRM
│       │   ├── SchemaService.cs        # Lectura de metadatos del datamart
│       │   └── SqlServerService.cs     # Conexion SQL, cache de BDs, ejecucion
│       ├── appsettings.json            # Configuracion principal (con API key)
│       ├── Program.cs                  # Configuracion DI y pipeline HTTP
│       └── DatamartAnalyzer.Api.csproj
├── frontend/
│   ├── src/
│   │   ├── App.jsx                     # Componente principal (toda la UI)
│   │   └── services/
│   │       └── api.js                  # Funciones de llamada al backend
│   ├── index.html
│   ├── package.json
│   ├── vite.config.js
│   └── tailwind.config.js
├── docs/
│   └── contexto-erp.md                 # Documento RAG del ERP (markdown, ~12,500 chars)
├── CLAUDE.md                           # Instrucciones para Claude Code
└── TECHNICAL.md                        # Este archivo
```

---

## 5. Backend — ASP.NET Core API

### Program.cs — Configuracion y Pipeline

```csharp
// Servicios registrados como Singleton (vida util = aplicacion)
builder.Services.AddSingleton<ISqlServerService, SqlServerService>();
builder.Services.AddSingleton<ISchemaService, SchemaService>();
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IPrebuiltQueriesService, PrebuiltQueriesService>();
builder.Services.AddHttpClient<IAnthropicService, AnthropicService>();

// Carga eagerly el documento RAG al iniciar (no lazy)
app.Services.GetRequiredService<IDocumentService>();
```

### SqlServerService — Gestion de Conexiones

**Responsabilidades**:
- `ProbarConexionAsync()`: Ping al servidor YOSEMITE (sin cargar BDs)
- `ObtenerBasesDatosAsync()`: Retorna cache si existe; si no, carga
- `RefrescarBasesDatosAsync()`: Fuerza recarga con `SemaphoreSlim(1,1)` para evitar carreras
- `ExisteBaseDatosAsync()`: Verifica cache primero, luego SQL
- `EjecutarQueryAsync()`: Ejecuta SELECT con timeout configurable y limite de filas

**Deteccion de bases datamart** (la parte mas costosa):

```csharp
// Verifica si la BD tiene la vista DefinicionesCamposTabla
SELECT COUNT(1) FROM INFORMATION_SCHEMA.VIEWS
WHERE TABLE_SCHEMA = 'ADP_DTM_VCONF'
  AND TABLE_NAME = 'DefinicionesCamposTabla'
```

Se ejecuta **en paralelo** para todas las BDs con `SemaphoreSlim(30)` + `Task.WhenAll()`,
reduciendo el tiempo de carga de cientos de conexiones secuenciales a ~segundos.

### SchemaService — Metadatos del Datamart

**Problema resuelto**: La vista `DefinicionesCamposTabla` puede tener columnas con nombres
distintos segun el datamart (ej: "Nombre Tabla" con espacio, vs "NombreTabla" sin espacio).

**Solucion**: Introspeccion dinamica via `INFORMATION_SCHEMA.COLUMNS` antes de ejecutar
la query principal. Se mapean candidatos en orden de preferencia:

```csharp
var colTabla = Resolver("Nombre Tabla", "NombreTabla", "TableName", "ViewName", "Tabla");
```

**Cache en memoria**: Schema por BD se cachea 10 minutos (`TimeSpan.FromMinutes(10)`).

### DocumentService — RAG (Retrieval Augmented Generation)

Lee el archivo `docs/contexto-erp.md` al iniciar la aplicacion:
- Archivo Markdown con ~12,500 caracteres (~3,100 tokens)
- Contiene: conceptos de negocio ADPRO, reglas criticas del datamart, mapeo ControlClaseOrigen, reglas de inventario, estados de proyecto, contexto SRM
- Expone el texto via `ObtenerContextoErp()`

El contenido se inyecta completo en el system prompt de cada consulta a Claude.

> **Cambio v1.1**: Reemplazado el `.docx` (DocumentFormat.OpenXml) por un `.md` plano,
> mas liviano y facil de mantener. El RAG se edita directamente en `docs/contexto-erp.md`.

### PrebuiltQueriesService — Consultas Preconstruidas

Servicio que intercepta preguntas frecuentes **antes** de llamar a la IA, eliminando el costo de tokens.

**Funcionamiento**:
1. `Match(pregunta)` compara la pregunta del usuario contra una lista de preguntas predefinidas
2. Si hay coincidencia (busqueda insensible a mayusculas/tildes), retorna el SQL directamente
3. El SQL se ejecuta; si falla, se hace fallback a la IA normal

**Consultas disponibles** (configuradas en codigo):

*ADPRO (8 consultas):*
- Costo ejecutado vs presupuestado por proyecto
- Desviacion presupuestal por capitulo
- Materiales e insumos con mayor consumo en proyectos en ejecucion
- Costo acumulado por tipo de recurso por proyecto
- Avance fisico vs costo ejecutado (ejecutado vs consumido)
- Top 20 proveedores por gasto en materiales
- Costo por metro cuadrado por proyecto en ejecucion
- Valor del inventario en proyectos en ejecucion (grafico Torta)

*SRM (10 consultas):*
- Procesos licitatorios por estado y proyecto
- Proveedores mas activos en licitaciones
- Licitaciones abiertas con plazo proximo a vencer
- Adjudicaciones por proveedor (monto total)
- Tiempo promedio de adjudicacion por proceso
- Procesos sin adjudicar vencidos
- Participacion de proponentes por licitacion
- Rondas de negociacion por proceso
- Habilitacion y estado de proveedores
- Adjudicaciones por estado del proveedor (grafico Torta)

**Marcado en la respuesta**: Las respuestas prebuilt incluyen `EsPrebuilt: true` y se muestran con badge verde "Instant" en la UI.

### AnthropicService — Integracion con Claude

**Headers HTTP enviados**:
```
x-api-key: <api_key>
anthropic-version: 2023-06-01
anthropic-beta: prompt-caching-2024-07-31
```

**Estructura del payload**:
```json
{
  "model": "claude-haiku-4-5-20251001",
  "max_tokens": 4096,
  "system": [
    {
      "type": "text",
      "text": "<instrucciones + documento contexto-erp.md>",
      "cache_control": { "type": "ephemeral" }
    },
    {
      "type": "text",
      "text": "<schema de la BD activa>",
      "cache_control": { "type": "ephemeral" }
    }
  ],
  "messages": [
    { "role": "user", "content": "<pregunta del usuario>" }
  ]
}
```

El system prompt se divide en dos bloques cacheables:
- **Bloque 1** (estatico): Instrucciones + contexto ERP — se cachea entre TODAS las consultas
- **Bloque 2** (por BD): Schema de la BD seleccionada — se cachea por cada BD diferente

**Extraccion de UsageInfo**: Cada llamada retorna una tupla `(AiRawResponse, UsageInfo?)`:
```csharp
public record UsageInfo(
    int TokensEntrada, int TokensSalida,
    int TokensCacheWrite, int TokensCacheRead,
    double CostoUsd, string Modelo
);
```

En el controller, si hay retry por error de columna, se acumulan los tokens de ambas llamadas.

---

## 6. Frontend — React SPA

Toda la UI vive en `App.jsx` como un unico componente principal con sub-componentes inline.

### Estados Principales

| Estado | Tipo | Descripcion |
|---|---|---|
| `bases` | `Array` | Lista de BDs disponibles |
| `dbSeleccionada` | `string` | BD activa |
| `schema` | `object` | Metadatos cargados de la BD |
| `mensajes` | `Array` | Historial del chat |
| `devMode` | `bool` | Muestra SQL generado (oculto por defecto) |
| `sqlMode` | `bool` | Modo SQL Directo (sin IA) |
| `showSuggestions` | `bool` | Panel de preguntas sugeridas |
| `sessionUsage` | `object` | Acumulado de tokens y costo en la sesion |
| `savedQueries` | `Array` | Consultas guardadas en historial (localStorage) |
| `vistaActiva` | `string` | Vista activa: `'consulta'` o `'dashboard'` |
| `dashboards` | `Array` | Dashboards personalizados (localStorage) |
| `favModal` | `object\|null` | Datos del modal de favorito abierto |

### Modos de Entrada

**Modo IA (default)**: El usuario escribe una pregunta en lenguaje natural. Se llama `POST /api/analyze`.
- Input de 2 filas, borde normal
- Envio con `Enter`

**Modo SQL Directo**: El usuario escribe SQL directamente. Se llama `POST /api/query`.
- Activado con el boton naranja `</>` en el input
- Input de 10 filas, fuente monospace, borde naranja
- Envio con `Ctrl+Enter`
- Respuesta marcada con badge naranja "SQL Directo"
- No consume tokens de IA

### Preguntas Sugeridas

Dos paneles con las mismas sugerencias:
1. **Estado vacio** (cuando se selecciona BD y no hay mensajes): panel central
2. **Panel flotante**: en la parte inferior, activado con el boton de bombilla

Ambos paneles tienen tabs **ADPRO** y **SRM** para alternar entre los dos conjuntos de preguntas.

### Historial de Consultas Guardadas

- Boton "Guardar consulta" aparece en respuestas con SQL
- Las consultas se guardan en `localStorage` con: pregunta, SQL, tipo de respuesta, configuracion de grafico
- Panel lateral "Historial" permite ejecutar consultas guardadas sin costo de IA
- Al ejecutar desde historial, la respuesta se marca con badge purpura "Ejecutado desde historial"

### Dashboard de Favoritos — Multiples Dashboards

#### Navegacion

En el header, junto al badge ADPRO, dos tabs:
- **Consulta** — vista principal de chat con IA
- **Dashboard ★** — vista de dashboards personalizados (badge con cantidad de dashboards)

#### Flujo para agregar un panel

1. El usuario hace una consulta y obtiene resultado con SQL
2. En el footer de la respuesta aparece el boton **★ Favorito** (junto a "Guardar consulta")
3. Al hacer clic se abre el **FavoriteModal** con:
   - Campo **Titulo del panel** (editable, pre-relleno con la pregunta)
   - Dropdown **Dashboard destino** con todos los dashboards existentes
   - Opcion "➕ Crear nuevo dashboard..." en el dropdown — muestra campo para nombre
4. Al confirmar, el panel se agrega al dashboard elegido (o se crea el dashboard nuevo)

#### Estructura de datos (localStorage: `datamart_dashboards`)

```json
[
  {
    "id": "dash_1234_abc",
    "nombre": "Costos de Obra",
    "paneles": [
      {
        "id": "pan_5678_xyz",
        "titulo": "Costo vs Presupuesto",
        "sql": "SELECT ...",
        "tipoRespuesta": "TablaMasGrafico",
        "grafico": { "tipo": "Barras", "campoEjeX": "...", "campoEjeY": "...", "titulo": "...", "colorPrimario": "#3b82f6" }
      }
    ]
  }
]
```

#### Vista Dashboard — componentes

**Header del Dashboard:**
```
[ Costos de Obra ▾ ]  BD: SincoSupervisor  3 paneles · 8 total    [ + Nuevo dashboard ]  [ Refrescar todo ]
```

**Dropdown selector de dashboards:**
- Lista todos los dashboards con conteo de paneles
- Clic para cambiar de dashboard activo
- Hover: botones de renombrar (inline) y eliminar por dashboard
- Opcion "Nuevo dashboard" al fondo

**Grid de paneles** — responsivo:
- 1 columna en movil
- 2 columnas en md (≥768px)
- 3 columnas en xl (≥1280px)

**Cada panel (DashboardPanel):**
- Header: icono drag (⠿), estrella, titulo truncado, boton reejecutar, boton quitar
- Body: resultado de la consulta (tabla compacta de 5 filas o grafico)
- Estado propio: loading / error / sin resultados / datos
- Se auto-ejecuta al montar

**Reordenamiento:** drag & drop nativo HTML5 dentro del dashboard activo

**Refrescar:** boton "Refrescar todo" re-monta todos los paneles con nueva `key`; tambien al entrar al tab

**Sin BD seleccionada:** mensaje de aviso, no ejecuta queries
**Dashboard vacio:** mensaje con boton "Ir a Consultas"

#### Ejecucion de paneles

Los paneles llaman directamente a `POST /api/query` (SQL directo, sin IA).
Costo de ejecucion del dashboard: **$0.00 USD**.

### Panel de Schema

Muestra las tablas (hechos y dimensiones) y vistas de la BD seleccionada.
Incluye **buscador** que filtra por nombre de tabla, nombre de campo o descripcion.

### Visualizacion de Respuestas

| TipoRespuesta | Renderizado |
|---|---|
| `Tabla` | DataTable con columnas ordenables |
| `Grafico` | Chart de Recharts (Barras/Lineas/Torta/Area) |
| `Texto` | Parrafo de texto |
| `TablaMasGrafico` | Tabla debajo del grafico |
| `TablaMasTexto` | Tabla + parrafo |
| `Error` | Mensaje de error en rojo |

### Graficos — Recharts

- **Barras, Lineas, Area**: contenedor de 300px de alto
- **Torta (PIE)**: contenedor de 420px de alto, `outerRadius=85`, `cy="42%"`, sin labels inline (solo Legend + Tooltip)
- **Overflow**: `overflow: 'hidden'` en el contenedor para que los SVG no se salgan
- **Tooltip**: fondo oscuro `#111827`, texto blanco `#f9fafb` aplicado con `labelStyle` + `itemStyle`
- **Eje Y**: formato compacto con `fmtAxis` (K/M para miles/millones)

### Formato de Numeros

- **Tablas y tooltips**: `Intl.NumberFormat('en-US')` — coma para miles, punto para decimal (ej: `1,234,567.89`)
- **Eje Y de graficos**: notacion compacta `fmtAxis` (ej: `1.2M`, `450K`)
- **Deteccion automatica de numericos**: si `typeof value === 'number'`, se alinea a la derecha con formato

### Costo de IA por Consulta

En el footer de cada respuesta IA (debajo del boton "Guardar consulta") se muestra:
```
1,234 tokens · costo IA: ~$0.00012 USD
```
- Solo visible si `usage.costoUsd > 0` (no aparece en prebuilt ni SQL directo)
- Tooltip con el modelo usado

### Costo de Sesion

En el header, badge acumulado de la sesion:
```
~$0.0214 USD
```
- Visible desde la primera consulta IA
- Tooltip con numero de consultas y total de tokens

---

## 7. Integracion con IA (Anthropic Claude)

### Modelo por Defecto

`claude-haiku-4-5-20251001` — Modelo rapido y economico, seleccionado por defecto en la UI.

El usuario puede cambiar el modelo desde el selector en el header:
- **Haiku 4.5**: rapido y barato (default)
- **Sonnet 4.6**: mas capaz, mayor costo
- **Opus 4.6**: mayor razonamiento, el mas caro

### Prompt de Sistema — Instrucciones a Claude

Claude recibe instrucciones explicitas para:

1. **Responder SOLO con JSON valido** — sin texto adicional ni markdown
2. **Usar solo SELECT** — nunca DML/DDL
3. **Usar nombres exactos** del schema proporcionado
4. **No inventar tablas** — solo las definidas en `DefinicionesCamposTabla`
5. **Usar llaves para JOINs** — campos marcados con `[LLAVE]`, siempre incluyendo `SkIdEmpresa`
6. **Aplicar reglas criticas** del datamart ADPRO (ControlClaseOrigen, inventario, estados)
7. **Usar el documento contexto-erp.md** para entender el negocio

### Estructura JSON de Respuesta de Claude

```json
{
  "TipoRespuesta": "Tabla | Grafico | Texto | TablaMasGrafico | TablaMasTexto",
  "ExplicacionTexto": "Descripcion en espanol para el usuario",
  "SqlGenerado": "SELECT ... FROM [ADP_DTM_FACT].[Tabla] ...",
  "Grafico": {
    "Tipo": "Barras | Lineas | Torta | Area | Dispersion",
    "CampoEjeX": "columna_x",
    "CampoEjeY": "columna_y",
    "CampoAgrupacion": null,
    "Titulo": "Titulo del grafico",
    "ColorPrimario": "#3b82f6"
  }
}
```

### Retry Automatico por Error de Columna

Si el SQL generado falla con error de "columna invalida", el sistema:
1. Llama a `CorregirSqlAsync()` con el SQL fallido y el mensaje de error
2. Claude genera un SQL corregido
3. Se re-ejecuta
4. El `UsageInfo` acumula los tokens de ambas llamadas

### Prompt Caching

Activado con el header `anthropic-beta: prompt-caching-2024-07-31`.

| Bloque | Contenido | Se cachea entre... |
|---|---|---|
| Bloque 1 | Instrucciones + contexto-erp.md | Todas las consultas |
| Bloque 2 | Schema de la BD | Consultas a la misma BD |

---

## 8. Base de Datos y Metadatos

### Servidor

- **Nombre**: YOSEMITE
- **Autenticacion**: Windows Authentication (Integrated Security=True)
- **TrustServerCertificate**: True (entorno interno)

### Vista de Metadatos del Datamart

```sql
[ADP_DTM_VCONF].[DefinicionesCamposTabla]
```

Esta vista existe en cada base de datos que sea un datamart ADPRO.
Su presencia es la condicion para que el sistema identifique una BD como "datamart".

**Columnas esperadas** (con variantes soportadas):

| Campo logico | Nombre en YOSEMITE | Alternativas soportadas |
|---|---|---|
| Esquema | `Esquema` | `Schema`, `TipoObjeto`, `ObjectType` |
| Nombre tabla | `Nombre Tabla` | `NombreTabla`, `TableName`, `Tabla` |
| Nombre columna | `Nombre Columna` | `NombreCampo`, `ColumnName`, `Campo` |
| Tipo de dato | `Tipo De Dato` | `TipoCampo`, `DataType`, `Tipo` |
| Descripcion | `Descripcion` | `DescripcionCampo`, `Description` |
| Es llave | `EsLlave` | `IsPrimaryKey`, `IsKey`, `Llave` |
| Formato | `Formato` | `Format` |

**Valores del campo Esquema**:
- `ADP_DTM_FACT` → tablas de hechos
- `ADP_DTM_DIM` → tablas de dimensiones

### Convencion de Llaves Surrogate

Si la columna `EsLlave` no existe en la vista, el sistema infiere que una columna es llave
surrogate si su nombre comienza con `SkId` (ej: `SkIdActividad`, `SkIdProyecto`).

---

## 9. Reglas Criticas del Datamart ADPRO

Estas reglas se encuentran en `docs/contexto-erp.md` y se inyectan en cada consulta a la IA.

### ControlClaseOrigen — Clasificacion de Movimientos

La tabla `[ADP_DTM_DIM].[ControlClaseOrigen]` clasifica cada movimiento de `[ADP_DTM_FACT].[ControlProyecto]`.
**Siempre filtrar por `[Clase]` para la clasificacion principal.**

| Clase | Descripcion |
|-------|-------------|
| `'P'` | Presupuestado |
| `'Y'` | Proyectado (ajustado) |
| `'L'` | Actas Cliente |
| `'C'` | Consumido |
| `'T'` | Asegurado contratos |
| `'B'` | Asegurado compras |
| `'J'` | Ejecutado (avance fisico) |
| `'I'` | Invertido (pagos) |

**PROHIBIDO**: usar `co.[Origen] = 'P'` — ese valor no existe en `[Origen]`, solo en `[Clase]`.

### Inventario en Obra

El inventario disponible NO es una Clase directa. Se calcula solo con movimientos fisicos:

```sql
SUM(CASE WHEN co.[Clase] = 'I' AND co.[Origen] IN ('E','NP','ED','V','VN','X','TE','TS','EX','SX','AJ')
         THEN cp.[Valor Total] ELSE 0 END)
- SUM(CASE WHEN co.[Clase] = 'C' AND co.[Origen] IN ('S','D')
         THEN cp.[Valor Total] ELSE 0 END) AS Inventario
```

No usar todos los origenes de `I` ni de `C` — solo los listados arriba.

### Columna de Valor

Usar siempre `[Valor Total]`, nunca `[Valor Sin IVA]`.

### JOINs con SkIdEmpresa

Todas las relaciones deben incluir `SkIdEmpresa` en el JOIN:
```sql
ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
```

### Estados de Proyecto

La columna `[ADP_DTM_DIM].[Proyecto].[Estado]` tiene **unicamente** estos cuatro valores:

| Estado | Descripcion |
|--------|-------------|
| `'Presupuesto'` | Proyecto en fase de presupuestacion |
| `'En ejecucion'` | Proyecto activo en curso |
| `'Inactivo'` | Proyecto pausado o suspendido |
| `'Finalizado'` | Proyecto terminado y cerrado |

**Proyectos "activos" o "en curso"** → `WHERE p.[Estado] = 'En ejecucion'`
**PROHIBIDO**: `'Activo'`, `'Terminado'`, `'En planeacion'`

---

## 10. Configuracion

### appsettings.json — Parametros Completos

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOSEMITE;Integrated Security=True;TrustServerCertificate=True;"
  },
  "Anthropic": {
    "ApiKey": "<tu-api-key>",
    "Model": "claude-haiku-4-5-20251001",
    "MaxTokens": 4096
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"]
  },
  "QuerySettings": {
    "TimeoutSeconds": 30,
    "MaxRows": 5000
  }
}
```

> **Nota v1.1**: La seccion `Rag:DocumentPath` fue eliminada. El RAG lee automaticamente
> `docs/contexto-erp.md` relativo al directorio del ejecutable.

### Descripcion de Parametros

| Parametro | Valor por Defecto | Descripcion |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | (requerido) | Cadena de conexion al servidor SQL |
| `Anthropic:ApiKey` | (requerido) | API Key de Anthropic (console.anthropic.com) |
| `Anthropic:Model` | `claude-haiku-4-5-20251001` | Modelo de Claude a usar |
| `Anthropic:MaxTokens` | `4096` | Maximo de tokens en la respuesta de Claude |
| `Cors:AllowedOrigins` | `["http://localhost:5173"]` | Origenes permitidos para CORS |
| `QuerySettings:TimeoutSeconds` | `30` | Timeout maximo de queries SQL en segundos |
| `QuerySettings:MaxRows` | `5000` | Limite de filas retornadas por query |

### Posibles Ajustes de Configuracion

**Cambiar el modelo de IA**:
```json
"Model": "claude-haiku-4-5-20251001"   // rapido y barato (default)
"Model": "claude-sonnet-4-6"           // mas capaz
"Model": "claude-opus-4-6"             // mayor razonamiento, mas caro
```

**Ajustar limite de filas**:
```json
"MaxRows": 10000   // permitir mas filas
"MaxRows": 1000    // limitar para respuestas rapidas
```

---

## 11. Seguridad

### Validaciones de SQL

El sistema aplica las siguientes validaciones antes de ejecutar cualquier SQL:

1. **Solo SELECT**: La query debe comenzar con `SELECT` o `WITH` (para CTEs)
2. **Blacklist de keywords**: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `EXEC`, `EXECUTE`, `SP_`
3. **Limite de filas automatico**: Si no tiene `TOP` ni `FETCH NEXT`, se inserta `TOP {maxRows}` automaticamente
4. **Timeout**: Toda query tiene un timeout maximo configurable (default 30s)

Estas validaciones aplican tanto al SQL generado por IA como al SQL ejecutado en modo directo.

### Autenticacion de Base de Datos

Windows Authentication: el proceso del servidor web se ejecuta con la identidad del usuario
Windows que tiene acceso al SQL Server YOSEMITE. No se almacenan ni exponen credenciales.

### Lo que NO se expone al Frontend

- Connection string de SQL Server
- API Key de Anthropic
- SQL generado por Claude (oculto por defecto, visible solo en modo Dev)

---

## 12. Rendimiento y Caching

### Cache de Lista de Bases de Datos

- **Tipo**: En memoria, Singleton (`SqlServerService`)
- **Invalidacion**: Manual mediante `POST /api/databases/refresh`
- **Problema resuelto**: 317+ BDs en YOSEMITE requerian conexiones secuenciales (~minutos)
- **Solucion**: `Task.WhenAll()` con `SemaphoreSlim(30)` — verificacion en paralelo (~segundos)

### Cache de Schema por Base de Datos

- **Tipo**: Dictionary en memoria, Singleton (`SchemaService`)
- **Duracion**: 10 minutos desde la ultima carga

### Consultas Preconstruidas

- Interceptan preguntas frecuentes **antes** de llamar a la IA
- Costo de IA: $0.00 (SQL hardcodeado, no hay llamada a Anthropic)
- Fallback automatico a IA si el SQL preconstruido falla en ejecucion

### Prompt Caching de Anthropic

- **Bloque 1** (instrucciones + contexto-erp.md): ~3,100 tokens, cacheable entre todas las consultas
- **Bloque 2** (schema de BD): variable, cacheable por BD activa
- **Ahorro con Haiku**: Primera consulta ~$0.004 USD; siguientes con cache ~$0.001 USD

---

## 13. Flujo Completo de una Consulta

```
1. Usuario abre la app
   └─► GET /api/databases/ping
       └─► Verifica conexion con YOSEMITE

2. Usuario abre el selector de BDs
   └─► GET /api/databases
       └─► Retorna lista desde cache (o carga si es primera vez)
       └─► Para cada BD: verifica [ADP_DTM_VCONF].[DefinicionesCamposTabla] (paralelo)

3. Usuario selecciona una BD
   └─► GET /api/schema/{database}
       └─► Verifica existencia de la BD
       └─► Consulta INFORMATION_SCHEMA.COLUMNS para nombres de columnas
       └─► SELECT * FROM [ADP_DTM_VCONF].[DefinicionesCamposTabla]
       └─► Mapea a SchemaColumn[] con tipos (HECHO/DIMENSION) y referencias completas

4a. Usuario escribe una pregunta (modo IA)
    └─► POST /api/analyze
        ├─► Intenta match en PrebuiltQueriesService
        │   └─► Si hay match: ejecuta SQL directo → retorna con EsPrebuilt=true
        └─► Si no hay match:
            ├─► Carga schema (desde cache si disponible)
            ├─► Construye payload para Claude con contexto-erp.md + schema
            ├─► Claude responde con JSON: TipoRespuesta + SQL + Grafico
            ├─► Ejecuta SQL; si falla por columna invalida → retry con CorregirSqlAsync
            └─► Retorna AnalyzeResponse con datos + UsageInfo

4b. Usuario escribe SQL (modo directo)
    └─► POST /api/query
        └─► Ejecuta SQL directamente sin IA
        └─► Retorna resultado (sin UsageInfo)

5. Frontend renderiza segun TipoRespuesta:
   ├─► Tabla: DataTable con columnas ordenables y numeros formateados (en-US)
   ├─► Grafico: Recharts con tipos de chart segun configuracion
   ├─► Texto: Parrafo en la burbuja del chat
   └─► Footer: tokens consumidos + costo individual (solo en respuestas IA)
```

---

## 14. API Endpoints

### Databases Controller

| Metodo | Endpoint | Descripcion |
|---|---|---|
| `GET` | `/api/databases` | Lista BDs (desde cache o carga inicial) |
| `POST` | `/api/databases/refresh` | Fuerza recarga del cache de BDs |
| `GET` | `/api/databases/ping` | Verifica conexion con YOSEMITE |

### Schema Controller

| Metodo | Endpoint | Descripcion |
|---|---|---|
| `GET` | `/api/schema/{database}` | Retorna metadatos completos del datamart |
| `GET` | `/api/schema/{database}/views` | Lista vistas disponibles en la BD |

### Analyze Controller

| Metodo | Endpoint | Descripcion |
|---|---|---|
| `POST` | `/api/analyze` | Procesa pregunta con IA (o prebuilt si hay match) |

**Response**:
```json
{
  "tipoRespuesta": "TablaMasGrafico",
  "explicacionTexto": "Los 10 proyectos con mayor costo ejecutado.",
  "sqlGenerado": "SELECT TOP 10 ...",
  "datos": [...],
  "grafico": { "tipo": "Barras", "campoEjeX": "Proyecto", "campoEjeY": "Total", "titulo": "..." },
  "mensajeError": null,
  "esPrebuilt": false,
  "usage": {
    "tokensEntrada": 450,
    "tokensSalida": 312,
    "tokensCacheWrite": 3100,
    "tokensCacheRead": 0,
    "costoUsd": 0.00412,
    "modelo": "claude-haiku-4-5-20251001"
  }
}
```

### Query Controller

| Metodo | Endpoint | Descripcion |
|---|---|---|
| `POST` | `/api/query` | Ejecuta SQL directo (modo avanzado, sin IA) |

---

## 15. Costos de la IA

### Precios Anthropic — Haiku 4.5 (modelo por defecto)

| Tipo de token | Precio por 1M tokens |
|---|---|
| Entrada (normal) | $0.80 USD |
| Escritura a cache | $1.00 USD |
| Lectura desde cache | $0.08 USD |
| Salida | $4.00 USD |

### Precios Anthropic — Sonnet 4.6

| Tipo de token | Precio por 1M tokens |
|---|---|
| Entrada (normal) | $3.00 USD |
| Escritura a cache | $3.75 USD |
| Lectura desde cache | $0.30 USD |
| Salida | $15.00 USD |

### Estimacion por Consulta (Haiku 4.5)

**Primera consulta del dia** (bloque 1 se escribe a cache):
- ~3,100 tokens cache_write + ~2,000 tokens schema + ~400 tokens salida
- ~$0.004 - $0.006 USD

**Consultas siguientes** (bloque 1 desde cache):
- ~3,100 tokens cache_read + ~2,000 tokens schema + ~400 tokens salida
- ~$0.001 - $0.002 USD

**Consultas prebuilt**: $0.00 USD

### Visibilidad de Costos en la UI

- **Por consulta**: Footer de cada respuesta IA muestra tokens + costo individual
- **Por sesion**: Badge en el header muestra acumulado de la sesion
- **En logs del backend**: Registro de tokens exactos por llamada

---

## 16. Despliegue

### Desarrollo (actual)

```bash
# Terminal 1 — Backend
cd datamart-analyzer/backend/DatamartAnalyzer.Api
dotnet run
# Escucha en: http://localhost:5000

# Terminal 2 — Frontend
cd datamart-analyzer/frontend
npm run dev
# Escucha en: http://localhost:5173
```

### Pre-requisitos

- .NET 9 SDK instalado
- Node.js 18+ instalado
- Acceso de red al servidor YOSEMITE (mismo dominio/red)
- Usuario Windows con permisos de lectura en las BDs de YOSEMITE
- API Key de Anthropic con credito disponible (console.anthropic.com)

### Build de Produccion (Frontend)

```bash
cd frontend
npm run build
# Genera dist/ con archivos estaticos
```

---

## 17. Troubleshooting

### Error de conexion a YOSEMITE

**Sintoma**: `GET /api/databases/ping` retorna `{ "connected": false }`

**Verificar**:
- El equipo esta en la misma red o dominio que YOSEMITE
- El usuario Windows del proceso tiene acceso al servidor
- Probar en SSMS: `Server=YOSEMITE; Authentication=Windows Authentication`

### Error de CORS

**Sintoma**: El frontend no puede llamar al backend (error de red en consola)

**Verificar**:
- El frontend corre en `http://localhost:5173`
- `Cors:AllowedOrigins` incluye exactamente ese origen (sin slash final)

### BD seleccionada retorna 404

**Causa**: La BD estaba en el cache pero fue eliminada o puesta offline en YOSEMITE

**Solucion**: Usar el boton de refresco `↻` para recargar la lista actualizada.

### Claude no genera SQL correcto

**Verificar**:
1. El schema se cargo correctamente: `GET /api/schema/{database}` retorna columnas
2. Los nombres de columna coinciden con las columnas reales de la BD
3. Revisar que `docs/contexto-erp.md` esta actualizado con las reglas criticas

### Error 401 de Anthropic API

**Causas**: API Key incorrecta o expirada

**Solucion**: Crear una nueva API Key en console.anthropic.com/settings/keys

### El backend no reinicia limpio

**Sintoma**: `dotnet run` falla con error de archivo bloqueado (MSB3026)

**Causa**: El proceso anterior sigue corriendo y bloquea el EXE

**Solucion**:
```powershell
Stop-Process -Name "DatamartAnalyzer.Api" -Force
# Esperar 2 segundos y volver a ejecutar dotnet run
```

---

## 18. Alcances del Proyecto

### Lo que el sistema HACE

- Conecta a SQL Server YOSEMITE con Windows Authentication
- Identifica automaticamente bases de datos que son datamarts ADPRO/SRM
- Lee la estructura de hechos y dimensiones desde `DefinicionesCamposTabla`
- Traduce preguntas en espanol a SQL usando Claude AI
- Ejecuta consultas preconstruidas para preguntas frecuentes (sin costo de IA)
- Permite ejecutar SQL directamente en modo avanzado (sin IA)
- Ejecuta las consultas y presenta resultados en tabla, grafico o texto
- Limita las consultas a SELECT (no permite modificar datos)
- Enriquece el contexto de la IA con documentacion del ERP (contexto-erp.md)
- Optimiza costos con prompt caching de Anthropic
- Cachea la lista de BDs y el schema en memoria
- Formatea numeros con locale en-US (coma miles, punto decimal)
- Permite ordenar columnas en tablas de resultados
- Guarda consultas frecuentes en localStorage para reutilizacion
- Muestra costo de IA por consulta y acumulado de sesion
- Muestra el SQL generado en modo desarrollador
- Permite crear multiples dashboards personalizados con paneles favoritos
- Dashboards ejecutan SQL directo sin costo de IA, con refresco manual o automatico al entrar
- Drag & drop para reordenar paneles dentro de un dashboard
- Renombrar y eliminar dashboards directamente desde el dropdown selector

### Lo que el sistema NO HACE (fuera de alcance actual)

- No tiene autenticacion de usuarios
- No exporta resultados a Excel o CSV
- No guarda historial de mensajes entre sesiones (solo localStorage de consultas y dashboards)
- No tiene roles o permisos por usuario
- No soporta consultas multi-BD
- No genera reportes programados
- Los dashboards no se sincronizan entre dispositivos (solo localStorage local)

---

## 19. Mejoras Futuras

### Prioritarias

- **Exportar a Excel**: Agregar endpoint con EPPlus o ClosedXML
- **Autenticacion**: Integrar con Active Directory usando NTLM o JWT

### Funcionales

- **Favoritos por usuario**: Guardar preguntas frecuentes con nombre personalizado
- **Cache de schema configurable**: Boton de refresco de metadatos en la UI
- **Feedback de respuestas**: Pulgar arriba/abajo para mejorar el prompt

### Tecnicas

- **Tests automatizados**: Unit tests para los servicios; integration tests para los controllers
- **Streaming de respuestas**: SSE para mostrar la respuesta de Claude mientras se genera
- **Rate limiting**: Limitar llamadas a Claude por IP
- **Health checks**: Endpoint `/health` con estado de SQL Server y Anthropic API

### Infraestructura

- **Docker**: Contenedorizar el backend para despliegue mas sencillo
- **Monitoreo de costos**: Dashboard de tokens consumidos por dia/mes
