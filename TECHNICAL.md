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
Los prebuilt hacen JOIN a `[ADP_DTM_DIM].[Proyecto]` con alias `p` y, cuando aplica, a `[ADP_DTM_DIM].[Insumo]` con alias `i`. Al aplicar filtros se inyecta WHERE automáticamente:

| Filtro | Tipo | Condición inyectada |
|--------|------|---------------------|
| empresa | `IN` | `p.[Empresa] IN ('...')` |
| proyecto | `IN` | `p.[Nombre Proyecto] IN ('...')` |
| macroproyecto | `IN` | `p.[MacroProyecto] IN ('...')` |
| estado | `IN` | `p.[Estado] IN ('...')` |
| insumo | `LIKE` | `i.[Insumo Descripcion] LIKE '%texto%' ESCAPE '\'` — **sólo si la SQL usa la dimensión Insumo** |

> El filtro **insumo** se comporta distinto: si la query no hace JOIN a `[ADP_DTM_DIM].[Insumo]` (por ejemplo "Listar proyectos con su estado"), el filtro se ignora silenciosamente en esa consulta — otras dimensiones siguen aplicando normal. Ver sección *Filtro de Insumo (texto / LIKE)* más abajo.

---

## Filtros de Contexto

Filtros persistentes en el header (visibles en vista Consulta y Dashboard con BD seleccionada). Orden jerárquico:

| Orden | Filtro | Input | Tabla origen | Columna valor | Display |
|-------|--------|-------|-------------|---------------|---------|
| 1 | Empresa | select multi | `[ADP_DTM_DIM].[Proyecto]` | `Empresa` | Nombre directo |
| 2 | Macroproyecto | select multi | `[ADP_DTM_DIM].[Proyecto]` | `MacroProyecto` (código) | `MacroProyecto Descripcion` |
| 3 | Proyecto | select multi | `[ADP_DTM_DIM].[Proyecto]` | `Nombre Proyecto` | Nombre directo |
| 4 | Estado | select multi | `[ADP_DTM_DIM].[Proyecto]` | `Estado` | Nombre directo |
| 5 | Insumo | **texto libre** | `[ADP_DTM_DIM].[Insumo]` | `Insumo Descripcion` | Texto entre comillas en el chip |

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

### Filtro de Insumo (texto / LIKE)

A diferencia de los otros cuatro filtros —que cargan valores discretos desde el backend y se inyectan con `IN (...)`—, **Insumo** es un campo de texto libre que se aplica como `LIKE '%texto%'` sobre la descripción del insumo. Está pensado para búsquedas parciales tipo "cemento", "varilla 3/8", "mano de obra".

**Configuración en `FILTER_CONFIG`:**
```javascript
{ tipo: 'insumo', label: 'Insumo', inputType: 'text',
  meta: { tabla: 'ADP_DTM_DIM.Insumo', columna: 'Insumo Descripcion' },
  placeholder: 'Buscar descripción (contains)...' }
```

La metadata es **hardcodeada** (no hay endpoint para cargarla) y se registra automáticamente en `filterMeta` al montar `ContextVarsBar`.

**Componente `FilterTextInput`:** popup compacto con input, botón *Aplicar* (o Enter) y *Limpiar*. El chip muestra el texto entre comillas (`"cemento"`). En el estado, se mantiene la convención de lista de strings para compatibilidad con el resto del sistema (`{ insumo: ['cemento'] }`).

**Inyección doble (prompt + verificación post-hoc):**

1. **En el prompt a Claude** (`AnthropicService.BuildMensajeUsuario`): se instruye a la IA a aplicar el filtro como `LIKE '%{texto}%'` sobre `[ADP_DTM_DIM].[Insumo].[Insumo Descripcion]`, indicando que si la consulta no involucra la dimensión Insumo debe ignorarlo.
2. **Verificación post-hoc** (`Controllers.InjectarFiltrosPrebuilt`): tras recibir el SQL generado por la IA, se vuelve a ejecutar la inyección. Si la IA omitió el filtro pero la SQL sí hace JOIN a Insumo, el backend lo agrega automáticamente. Si la IA ya incluyó `[Insumo Descripcion] LIKE`, se evita la duplicación.

**Escape para `LIKE`:** `EscaparParaLike` procesa en este orden `\ → \\` (primero, para evitar doble-escape), `% → \%`, `_ → \_`, `' → ''` (escape SQL estándar). Se emite con `ESCAPE '\'` para que SQL Server interprete el backslash como carácter de escape dentro del patrón.

**Detección de dimensión Insumo:**
- `SqlUsaDimensionInsumo(sql)`: regex `\[?ADP_DTM_DIM\]?\.\[?Insumo\]?\b` — tolera corchetes opcionales.
- `DetectarAliasInsumo(sql)`: regex `\[?ADP_DTM_DIM\]?\.\[?Insumo\]?\s+(?:AS\s+)?(\w+)` — extrae el alias; fallback `i` (convención del proyecto).

**Ignorar silenciosamente:** si el SQL no referencia `[ADP_DTM_DIM].[Insumo]`, el filtro se salta sin warning. Esta decisión de UX evita que consultas como "Listar proyectos con su estado" fallen porque un filtro de insumo estaba activo.

---

## Marcas de Compatibilidad de Filtros

Cada tarjeta de **pregunta sugerida**, **consulta guardada** y **panel del dashboard** muestra cinco dots de colores (uno por filtro) que indican qué filtros globales aplican a esa consulta. Es una capa informativa, no cambia el comportamiento de inyección (que sigue siendo automático con regex sobre el SQL).

### Detección

| Origen | Método | Precisión |
|--------|--------|-----------|
| Consulta guardada / generada por IA | Regex sobre `sql` — `detectarFiltrosDesdeSql` | Exacta (mismo regex que el backend) |
| Pregunta sugerida (prebuilt) | Heurística sobre texto de la pregunta — `detectarFiltrosDesdePregunta` | Aproximada: asume que todos los prebuilts unen Proyecto; activa `insumo` si hay keywords (`insumo`, `material`, `APU`, `DUC`, `III`, `IONC`, `IRI`, `RCI`) |
| Panel de dashboard | Prebuilt → heurística; SQL libre → regex | Según origen |

Los prebuilts no exponen su SQL al cliente (está server-side en `PrebuiltQueriesService`), de ahí la heurística. La inyección real y la decisión final de "ignorar si no aplica" siguen en el backend.

### Visualización (A + D — híbrido estático + contextual)

**A) Dots estáticos:** siempre visibles en cada card. Cada dot usa el `dotBg` del filtro en `FILTER_CONFIG` (azure=empresa, purple=macroproyecto, emerald=proyecto, amber=estado, cyan=insumo). Filtros no soportados se muestran con opacidad reducida (`opacity-20`).

**D) Señalización contextual:** cuando hay filtros globales activos que la tarjeta NO soporta:
- El dot correspondiente recibe un `ring` ámbar
- Aparece un pequeño contador `!N` al final (N = filtros activos ignorados)
- La tarjeta en su conjunto recibe un borde/ring ámbar sutil:
  - Sugerencias: `ring-1 ring-amber-500/40` en la card
  - Guardadas: `border-l-2 border-amber-500/40` en la fila
  - Panel de dashboard: `border-amber-500/30` en el contenedor

Cuando no hay filtros activos, sólo se ven los dots (A). Cuando los hay, la señal contextual (D) destaca qué tarjetas ignorarán alguna de las selecciones actuales del usuario.

### Caso especial: `aceptaFiltrosGlobales = false`

En paneles del dashboard con los filtros globales desactivados, las marcas siguen mostrando los filtros que el panel *soportaría* (para referencia), pero el anillo ámbar contextual se desactiva — el panel no ignora los filtros por incompatibilidad sino por decisión explícita del usuario, así que no hay razón para advertir.

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
| `FILTER_CONFIG` | Definición de los 5 filtros con estilos Tailwind (orden jerárquico). Cada entry incluye `inputType: 'select' \| 'text'` para determinar el componente del popup |
| `FILTER_CHILDREN` | Mapa de hijos jerárquicos `{ empresa: ['macroproyecto','proyecto'], macroproyecto: ['proyecto'] }` |
| `FilterDropdown` | Popup multiselección con búsqueda, filtrado jerárquico client-side y tooltip custom (filtros `inputType: 'select'`) |
| `FilterTextInput` | Popup con input de texto libre + Aplicar/Limpiar, para filtros `inputType: 'text'` (ej. Insumo) |
| `ContextVarsBar` | Barra de filtros; mantiene `labelMap` para chips select, registra meta hardcodeada de filtros text al montar, y ramifica entre `FilterDropdown` y `FilterTextInput` según `inputType` |
| `FilterSupportBadges` | 5 dots con dim/opacidad para indicar qué filtros soporta una consulta; marca en ámbar los filtros activos que serían ignorados |
| `detectarFiltrosDesdeSql` / `detectarFiltrosDesdePregunta` | Helpers que devuelven la lista de filtros soportados: regex sobre SQL (exacto) o heurística sobre la pregunta (para prebuilts) |
| `Dashboard` | Vista de paneles guardados con filtros globales pendientes/aplicados |
| `DashboardPanel` | Panel individual; aplica o ignora filtros globales según `aceptaFiltrosGlobales` |
| `SchemaPanel` | Panel lateral con estructura del datamart |
| `THEMES` + `useTheme` | Array de temas (Oscuro/Noche/Light) y hook que persiste en `localStorage` |

### Estados principales (App.jsx)

```javascript
selectedDb       // BD seleccionada
contextVars      // { empresa:[], macroproyecto:[], proyecto:[], estado:[], insumo:[] }
filterMeta       // { [tipo]: { tabla, columna } }
vistaActiva      // 'consulta' | 'dashboard'
showSuggestions  // panel de preguntas sugeridas
messages         // historial del chat
schema / vistas  // metadatos de la BD activa
theme            // 'dark' | 'night' | 'day' (persistido en localStorage)
// Dashboard:
pendingFilters   // filtros pendientes (aún no aplicados a paneles)
appliedFilters   // filtros efectivamente aplicados a paneles
```

---

## Temas / UI (`index.css` + `useTheme`)

La app soporta tres temas que ciclan al clicar el toggle del header. El estado se persiste en `localStorage` bajo la clave `app-theme`. La implementación se basa en **CSS custom properties** declaradas en `:root` (dark por defecto) y sobreescritas por las clases `.theme-night` y `.theme-day` aplicadas al elemento `<html>`.

| Tema | ID interno | Label visible | Paleta |
|------|-----------|---------------|--------|
| Oscuro | `dark`  | Oscuro | Carbón suave + azul (`#090d16` bg, `#3b82f6` acento) |
| Noche  | `night` | Noche  | Negro neutro + dorado (`#0f0f11` bg, `#ffb800` acento) |
| Light  | `day`   | **Light** | Slate neutro + azul saturado (`#F9FAFB` bg, `#2563EB` acento) |

> El ID interno del tema claro sigue siendo `day` para mantener compatibilidad con valores ya guardados en `localStorage`; sólo el `label` visible cambió de "Día" a "Light".

### Sistema de tokens CSS

Las variables se organizan en tres escalas semánticas:

| Escala | Propósito |
|--------|-----------|
| `--ink-950` … `--ink-600` | Superficies/backgrounds, de menor a mayor elevación |
| `--gray-200` … `--gray-900` | Texto y bordes; **invertido en light** (`gray-200` = texto primario oscuro) |
| `--azure-400` … `--azure-600` | Color de acento (azul en dark/light, dorado en night) |

Extras: `--body-bg`, `--body-color`, `--scrollbar-track/thumb/hover`, `--grid-rgb`.

### Paleta Light (slate neutro)

El tema `day` fue rediseñado con una paleta slate profesional orientada a datos (en lugar del azul-cielo anterior):

| Token | Valor | Uso |
|-------|-------|-----|
| `--ink-950` | `#F9FAFB` | Fondo base de página |
| `--ink-900` | `#FFFFFF` | Cards / surfaces |
| `--ink-800` | `#F1F5F9` | Zebra striping, surface-2 |
| `--ink-700` | `#E2E8F0` | Hover, borde sutil |
| `--gray-200` | `#0F172A` | Texto primario |
| `--gray-300` | `#475569` | Texto secundario |
| `--azure-400` | `#2563EB` | Acento CTA |

### Overrides puntuales del tema Light

Como `day` invierte la convención (superficies claras / texto oscuro), necesita overrides explícitos para clases translúcidas que no derivan sólo de variables: `text-white/*`, `bg-white/*`, `border-white/*`, `text-gray-{200..500}`. Estos viven en el bloque "Day theme — overrides puntuales" de `index.css`.

### Header bloqueado en dark (en los tres temas)

El `<header>` conserva la estética oscura original (fondo `#1B344C` inline + texto claro) independiente del tema activo. Se logra reescalando las CSS variables dentro del ámbito del header:

```css
.theme-day header,
.theme-night header {
  --ink-950: 9 13 22;
  --ink-900: 14 20 38;
  --ink-800: 22 30 50;
  /* ... */
  --gray-200: 229 231 235;
  --gray-300: 209 213 219;
  /* ... */
  --azure-400: 96 165 250;
  --azure-500: 59 130 246;
}
```

Las variables reseteadas propagan a todos los descendientes (incluyendo los dropdowns del selector de BD, que están en el DOM dentro del `<header>` aunque floten por `position: absolute`). Esto **neutraliza los overrides de `.theme-day` automáticamente**, porque esos overrides referencian las mismas variables que acabamos de reescalar — no fue necesario tocar JSX ni añadir `!important`.

### Hook `useTheme`

```javascript
const { theme, cycleTheme } = useTheme()
// theme:       'dark' | 'night' | 'day'
// cycleTheme:  cicla al siguiente tema, actualiza className en <html> y localStorage
```

El orden del ciclo está definido por el array `THEMES` en `App.jsx`:

```javascript
const THEMES = [
  { id: 'dark',  label: 'Oscuro', Icon: Moon,    iconClass: 'text-slate-400'  },
  { id: 'night', label: 'Noche',  Icon: SunMoon, iconClass: 'text-indigo-400' },
  { id: 'day',   label: 'Light',  Icon: Sun,     iconClass: 'text-amber-400'  },
]
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
