# Datamart Analyzer — conector MCP

Permite consultar datamarts de ADPRO/SincoERP en lenguaje natural. Este repo es
hoy, sobre todo, **el conector para Claude Desktop**: el backend y el frontend ya
se integraron al ERP y viven en Azure DevOps.

| Pieza | Dónde |
|---|---|
| Conector MCP + su Core | acá |
| Backend | `ADPRO.Core.API`, rama `Feature_VisualizadorDM`, en `Features/DatamartAnalyzer` |
| Frontend | `ADPRO.Informes`, rama `DatamartAnalyzer`, en `src/pages/datamart` |

`backend/DatamartAnalyzer.Api` y `frontend/` siguen acá como **referencia del
original**. No son lo que corre en el ERP.

## Estructura

```
backend/
  DatamartAnalyzer.Core/         Sql, Schema, Store, Rag, Prebuilt, Charts, Models
  DatamartAnalyzer.Mcp/          el conector (stdio, ModelContextProtocol 2.1.0)
  DatamartAnalyzer.Migrations/   scripts .sql del esquema ADP_DTM_APP
  DatamartAnalyzer.Api/          referencia del original (controllers MVC)
connector/manifest.json          manifiesto del paquete .mcpb
scripts/empaquetar-conector.ps1  arma el .mcpb (win y mac)
scripts/permisos-mcpb.js         permisos Unix del zip, necesario en macOS
sql/declarar-datamart.sql        declara el datamart en dbo.ADPConfig del ERP
docs/contexto-erp.md             corpus de negocio (RAG), viaja embebido
```

## Empaquetar

```
powershell -ExecutionPolicy Bypass -File scripts\empaquetar-conector.ps1 -Plataforma ambas -Version X.Y.Z
```

Sale en `dist\`. Se instala en Claude Desktop desde Configuración → Extensiones →
Advanced settings → Install Extension. **Apagar la extensión antes de reinstalar**,
o el `.mcpb` falla con `EPERM` porque el proceso del conector retiene `coreclr.dll`.

El paquete de macOS sale **sin firmar**: macOS lo pone en cuarentena. Ver
`docs/CONECTOR-MAC.md` antes de entregarlo a un cliente.

## Los dos modos del conector

Se eligen por configuración, detrás de la misma interfaz `IBackend`:

- **SQL directo** — el usuario final carga cuatro datos (servidor, base, usuario,
  clave) y el conector habla con SQL Server aplicando las mismas reglas de `Core`
  que aplicaría la API. **Usuario y clave vacíos = autenticación de Windows**, y
  es el caso esperado en la red interna.
- **Vía API** — hay un Datamart Analyzer corriendo; hereda su modo ERP, su IA y
  su almacén.

## Reglas del conector

- **Todo log va a stderr.** `stdout` transporta el protocolo MCP: una línea suelta
  ahí rompe la sesión entera.
- `InvariantGlobalization` debe quedar en `false`. Con `true` compila igual y
  revienta en la primera consulta con "Globalization Invariant Mode is not
  supported".
- El conector **nunca** reporta ni loguea la clave.
- Los `LogicalName` de los scripts embebidos (`DatamartAnalyzer.Migrations.Scripts.*`)
  son la identidad del script en la tabla `SchemaVersions` de cada datamart.
  Cambiarlos hace correr de nuevo las migraciones sobre almacenes ya instalados.

## Cuidado con Core

`DatamartAnalyzer.Core` existe **dos veces**: acá y, ya evolucionado, en
`Features/DatamartAnalyzer` de `ADPRO.Core.API` (net8, Carter, enums como texto).
Los dos escriben sobre las mismas tablas `ADP_DTM_APP`. Al tocar `Core`, decidir
si el cambio también tiene que ir del otro lado.

## Trampas de PowerShell 5.1

Los scripts de este repo corren en Windows PowerShell 5.1: no existe el operador
ternario `? :`, los nombres de variable **no distinguen mayúsculas** (`$minX` y
`$MinX` son la misma), y un archivo escrito sin BOM se lee como ANSI y mangla los
acentos.

## Estado

`main`, commit `56c5cae`, con **4 commits sin subir**: el push a GitHub pide un
Personal Access Token, no contraseña.
