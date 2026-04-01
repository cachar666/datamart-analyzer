# Datamart Analyzer — Documentación Técnica

## Stack

| Capa | Tecnología |
|------|-----------|
| Backend | ASP.NET Core 8, C#, Dapper |
| Base de datos | SQL Server en YOSEMITE (Windows Auth) |
| Frontend | React 18 + Vite + Tailwind CSS + Recharts |
| IA | Anthropic Claude API (`claude-sonnet-4-20250514`) |

---

## Arquitectura

```
datamart-analyzer/
├── backend/DatamartAnalyzer.Api/
│   ├── Controllers/Controllers.cs      — endpoints REST
│   ├── Services/
│   │   ├── AnthropicService.cs         — integración Claude API
│   │   ├── SqlServerService.cs         — conexión y ejecución SQL
│   │   ├── SchemaService.cs            — carga metadatos del datamart
│   │   ├── PrebuiltQueriesService.cs   — queries preconstruidas (sin IA)
│   │   └── DocumentService.cs          — contexto RAG del ERP
│   └── Models/Models.cs                — records y enums compartidos
└── frontend/src/
    ├── App.jsx                         — componente principal (~2200 líneas)
    └── services/api.js                 — cliente HTTP (axios)
```

---

## Endpoints API

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/databases` | Lista BDs del servidor YOSEMITE |
| GET | `/api/schema/{database}` | Metadatos de hechos y dimensiones |
| POST | `/api/analyze` | Procesa pregunta → SQL → resultado |
| POST | `/api/query` | Ejecuta SQL directo (modo avanzado) |
| POST | `/api/analyze/execute` | Ejecuta SQL prebuilt con filtros inyectados |
| GET | `/api/views/{database}` | Vistas disponibles en el datamart |
| GET | `/api/filters/{database}/{tipo}` | Valores distintos para filtros de contexto |

---

## Flujo Analyze (`POST /api/analyze`)

```
Pregunta
  │
  ├─► ¿Coincide con prebuilt?
  │     ├─ SÍ → Inyectar filtros → Ejecutar SQL → Retornar (sin IA)
  │     │        └─ Si falla → Retornar error (nunca hace fallback a IA)
  │     └─ NO → Claude API → SQL generado → Ejecutar → Retornar
  │                           └─ Si falla por columna → Retry con corrección
```

### Tipos de respuesta (TipoRespuesta)
- `Tabla` — grid de datos
- `Grafico` — Recharts (Barras, Líneas, Torta, Área, Dispersión)
- `Texto` — respuesta narrativa
- `TablaMasGrafico` — tabla + gráfico combinados
- `TablaMasTexto` — tabla + explicación
- `Error` — mensaje de error

---

## Queries Preconstruidas (PrebuiltQueriesService)

Consultas con match exacto por texto normalizado → cero costo de IA.

| Pregunta | Tipo |
|----------|------|
| ¿Cuál es el costo ejecutado vs presupuestado por proyecto de construcción? | TablaMasGrafico |
| ¿Cuáles son los capítulos o ítems de obra con mayor desviación entre presupuesto y ejecución? | TablaMasGrafico |
| ¿Qué materiales e insumos tienen mayor consumo en los proyectos activos? | TablaMasGrafico |
| ¿Cuál es el costo acumulado por tipo de recurso por proyecto? | TablaMasGrafico |
| ¿Cómo ha evolucionado el gasto mensual de obra en cada proyecto este año? | TablaMasGrafico |
| ¿Cuáles son los contratos de subcontratistas con mayor valor ejecutado vs contratado? | Tabla |
| ¿Qué proveedores concentran el mayor gasto en materiales de construcción? | TablaMasGrafico |
| ¿Cuál es el costo por metro cuadrado construido en cada proyecto activo? | Tabla |
| ¿Cuál es el valor del inventario de mis proyectos en ejecución? | TablaMasGrafico |
| Listar proyectos con su respectivo estado | Tabla |
| Listar los macroproyectos del sistema incluyendo la empresa y sus respectivos codigos | Tabla |
| + queries SRM (licitaciones, adjudicaciones, proveedores) | Tabla / TablaMasGrafico |

### Inyección de filtros en prebuilt
Los prebuilt hacen JOIN a `[ADP_DTM_DIM].[Proyecto]` con alias `p`. Al aplicar filtros se inyecta WHERE automáticamente:

| Filtro | Columna inyectada |
|--------|------------------|
| empresa | `p.[Empresa]` |
| proyecto | `p.[Nombre Proyecto]` |
| macroproyecto | `p.[MacroProyecto]` |
| estado | `p.[Estado]` |

---

## Filtros de Contexto

Filtros persistentes en el header (visibles en vista Consulta y Dashboard con BD seleccionada). Orden jerárquico:

| Orden | Filtro | Tabla origen | Columna valor | Display |
|-------|--------|-------------|---------------|---------|
| 1 | Empresa | `[ADP_DTM_DIM].[Proyecto]` | `Empresa` | Nombre directo |
| 2 | Macroproyecto | `[ADP_DTM_DIM].[Proyecto]` | `MacroProyecto` (código) | `MacroProyecto Descripcion` |
| 3 | Proyecto | `[ADP_DTM_DIM].[Proyecto]` | `Nombre Proyecto` | Nombre directo |
| 4 | Estado | `[ADP_DTM_DIM].[Proyecto]` | `Estado` | Nombre directo |

### Jerarquía en cascada
- Al cambiar **Empresa** → se limpian automáticamente Macroproyecto y Proyecto seleccionados
- Al cambiar **Macroproyecto** → se limpia automáticamente Proyecto seleccionado
- **Estado** es independiente (no participa en la jerarquía)

Al abrir el dropdown de un filtro hijo, sus opciones se reducen según los padres activos (filtrado client-side con `empresaPorValor` y `metadataPorValor`).

### Enriquecimiento visual por tipo

| Filtro | Subtext | Tooltip |
|--------|---------|---------|
| Macroproyecto | Código numérico | Empresa(s) a la que pertenece |
| Proyecto | Código de proyecto | Empresa · Macroproyecto (descripción) — tooltip custom posición fixed |

El tooltip del filtro Proyecto es un componente custom con `position: fixed` para escapar el `overflow-hidden` del dropdown.

Valores cargados vía `GET /api/filters/{database}/{tipo}` con mapeo explícito. El backend retorna:
- `valores` — lista de códigos/nombres para SQL injection
- `empresaPorValor` — `{ código: [empresas] }` (macroproyecto)
- `descripcionPorValor` — `{ código: descripción }` (macroproyecto)
- `metadataPorValor` — `{ nombre: { codigo, empresa, macroproyecto } }` (proyecto, con descripción del macro)

Se envían en cada request como `contextoVariables` y `filterMeta` (tabla/columna para el prompt IA).

---

## Dashboard

- Paneles guardados como "favoritos" con SQL base, tipo de respuesta y gráfico
- Cada panel puede aceptar o ignorar los filtros globales del dashboard (`aceptaFiltrosGlobales`)
- Barra de filtros en el dashboard con estado **pendiente** / **aplicado** (split): los paneles solo se re-ejecutan al presionar "Aplicar"
- Paneles prebuilt: usan `POST /api/analyze` con la pregunta clave para aprovechar el SQL preconstruido con filtros
- Paneles SQL libre: usan `POST /api/analyze/execute` con filtros inyectados cuando hay filtros activos

---

## Seguridad SQL

- Solo `SELECT` y `WITH` (CTEs) permitidos
- Palabras bloqueadas: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `EXEC`, `EXECUTE`, `SP_`
- Se agrega `TOP {maxRows}` automáticamente respetando `DISTINCT`
- Timeout 30s, max 5000 filas (configurable)

---

## Componentes Frontend

| Componente | Descripción |
|-----------|-------------|
| `FILTER_CONFIG` | Definición de los 4 filtros con estilos Tailwind (orden jerárquico) |
| `FILTER_CHILDREN` | Mapa de hijos jerárquicos `{ empresa: ['macroproyecto','proyecto'], macroproyecto: ['proyecto'] }` |
| `FilterDropdown` | Dropdown multiselección con búsqueda, filtrado jerárquico client-side y tooltip custom |
| `ContextVarsBar` | Barra de filtros; mantiene `labelMap` para mostrar descripciones en chips |
| `Dashboard` | Vista de paneles guardados con filtros globales pendientes/aplicados |
| `DashboardPanel` | Panel individual; aplica o ignora filtros globales según `aceptaFiltrosGlobales` |
| `SchemaPanel` | Panel lateral con estructura del datamart |

### Estados principales (App.jsx)

```javascript
selectedDb       // BD seleccionada
contextVars      // { empresa:[], macroproyecto:[], proyecto:[], estado:[] }
filterMeta       // { [tipo]: { tabla, columna } }
vistaActiva      // 'consulta' | 'dashboard'
showSuggestions  // panel de preguntas sugeridas
messages         // historial del chat
schema / vistas  // metadatos de la BD activa
// Dashboard:
pendingFilters   // filtros pendientes (aún no aplicados a paneles)
appliedFilters   // filtros efectivamente aplicados a paneles
```

---

## Inicio en Desarrollo

```powershell
Start-Process cmd "/k cd /d C:/Proyectos/AA/datamart-analyzer/backend/DatamartAnalyzer.Api && dotnet run"
Start-Process cmd "/k cd /d C:/Proyectos/AA/datamart-analyzer/frontend && npm run dev"
```

- Backend: `https://localhost:7001`
- Frontend: `http://localhost:5173` (o 5174 si el puerto está ocupado)

---

## Configuración (appsettings.Development.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOSEMITE;Integrated Security=True;TrustServerCertificate=True;"
  },
  "Anthropic": {
    "ApiKey": "...",
    "Model": "claude-sonnet-4-20250514",
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
