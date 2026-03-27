# Datamart Analyzer — Instrucciones para Claude Code

## Descripción del Proyecto

Aplicación fullstack que permite a usuarios analizar datamarts de ERP (ADPRO/SincoERP) mediante lenguaje natural, con soporte de IA (Anthropic Claude). El sistema se conecta al servidor SQL Server **[YOSEMITE]** con autenticación de Windows, lee la estructura de hechos y dimensiones desde la vista `ADP_DTM_VCONF.DefinicionesCamposTabla`, y responde preguntas del usuario con consultas SQL, gráficos, texto o combinaciones.

## Arquitectura

```
datamart-analyzer/
├── backend/   → ASP.NET Core 8 Web API (C#)
└── frontend/  → React + Vite (SPA)
```

## Stack Tecnológico

- **Backend**: C# / ASP.NET Core 8 / Entity Framework Core / Dapper
- **Base de datos**: SQL Server en servidor YOSEMITE (Windows Authentication)
- **Frontend**: React 18 + Vite + Tailwind CSS + Recharts
- **IA**: Anthropic Claude API (claude-sonnet-4-20250514)

## Configuración Inicial

### 1. Variables de Entorno (Backend)

Crear archivo `backend/DatamartAnalyzer.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOSEMITE;Integrated Security=True;TrustServerCertificate=True;"
  },
  "Anthropic": {
    "ApiKey": "TU_API_KEY_AQUI",
    "Model": "claude-sonnet-4-20250514",
    "MaxTokens": 4096
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"]
  }
}
```

### 2. Instalar dependencias Backend

```bash
cd backend/DatamartAnalyzer.Api
dotnet restore
dotnet build
```

### 3. Instalar dependencias Frontend

```bash
cd frontend
npm install
```

### 4. Ejecutar en desarrollo

Terminal 1 (Backend):
```bash
cd backend/DatamartAnalyzer.Api
dotnet run
# Corre en: https://localhost:7001
```

Terminal 2 (Frontend):
```bash
cd frontend
npm run dev
# Corre en: http://localhost:5173
```

## Estructura de Base de Datos

### Vista Principal de Metadatos

La vista `ADP_DTM_VCONF.DefinicionesCamposTabla` en cada base de datos de datamart contiene:

| Columna | Descripción |
|---------|-------------|
| NombreTabla | Nombre de la tabla/vista del datamart |
| NombreCampo | Nombre del campo/columna |
| TipoCampo | Tipo de dato SQL |
| TipoObjeto | 'HECHO' o 'DIMENSION' |
| DescripcionCampo | Descripción legible del campo |
| EsLlave | Indica si es llave primaria/foránea |
| Formato | Formato de presentación |

> **Nota**: Si la vista tiene columnas con nombres distintos, ajustar el modelo `SchemaColumn.cs` y la query en `SchemaService.cs`.

### Vistas Adicionales del ERP

Cada base de datos puede contener vistas adicionales en el esquema `ADP_DTM_VCONF` que representan documentos del ERP (facturas, órdenes, inventario, etc.). El sistema las descubre automáticamente.

## Flujo de la Aplicación

1. Usuario selecciona base de datos del servidor YOSEMITE
2. Sistema carga metadatos desde `ADP_DTM_VCONF.DefinicionesCamposTabla`
3. Usuario escribe pregunta en lenguaje natural
4. Backend envía a Claude: contexto del schema + pregunta del usuario
5. Claude genera: SQL query + tipo de respuesta (tabla/gráfico/texto)
6. Backend ejecuta SQL y devuelve resultados
7. Frontend renderiza: tabla interactiva, gráfico Recharts, o texto según indicación de IA

## Endpoints API

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/databases` | Lista bases de datos del servidor YOSEMITE |
| GET | `/api/schema/{database}` | Carga metadatos de hechos y dimensiones |
| POST | `/api/analyze` | Procesa pregunta con IA y retorna resultado |
| POST | `/api/query` | Ejecuta SQL directamente (modo avanzado) |
| GET | `/api/views/{database}` | Lista vistas disponibles en el datamart |

## Prompt de Sistema para Claude (IA)

El prompt que se envía a Claude incluye:
- Estructura completa de tablas (hechos y dimensiones)
- Instrucción de responder SOLO en JSON con estructura definida
- Ejemplos de tipos de respuesta válidos
- Restricciones de seguridad (solo SELECT, no DML)

## Consideraciones de Seguridad

- Solo se permiten queries `SELECT` (whitelist de keywords)
- Timeout máximo de query: 30 segundos
- Windows Authentication maneja la autorización a nivel de BD
- No se expone el connection string al frontend

## Comandos Útiles Claude Code

```bash
# Verificar conexión a YOSEMITE
dotnet run --project backend/DatamartAnalyzer.Api -- --test-connection

# Ejecutar tests
dotnet test

# Build producción frontend
cd frontend && npm run build

# Ver logs detallados
dotnet run --verbosity detailed
```

## Tareas Pendientes / TODOs

- [ ] Agregar autenticación de usuario en el frontend (si se requiere)
- [ ] Exportar resultados a Excel
- [ ] Historial de consultas por sesión
- [ ] Favoritos de consultas frecuentes
- [ ] Modo oscuro
- [ ] Caché de metadatos por base de datos

## Troubleshooting

**Error de conexión a YOSEMITE:**
- Verificar que el equipo está en la misma red/dominio
- Confirmar que el usuario Windows tiene acceso al servidor SQL
- Probar con SSMS primero: `Server=YOSEMITE; Windows Auth`

**Error de CORS:**
- Verificar que el frontend corre en `http://localhost:5173`
- Ajustar `AllowedOrigins` en `appsettings.Development.json`

**IA no genera SQL correcto:**
- Revisar que los metadatos se cargan correctamente desde `DefinicionesCamposTabla`
- Aumentar el contexto en `AnthropicService.cs` si las tablas son muy grandes
