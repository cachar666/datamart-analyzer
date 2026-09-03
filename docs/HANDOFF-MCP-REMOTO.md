# Handoff — Conector MCP remoto + evolucion de la web

> Contexto para arrancar una sesion de Claude Code en modo plan.
> Origen: sesion de Cowork del 3 de septiembre de 2026.
> Todo lo de aqui fue verificado contra el equipo `adp-005`, no supuesto.

## 1. Alcance: dos frentes en el mismo worktree

**Frente A — que el conector funcione desde el celular.**
Hoy `Datamart ADPRO` es un MCP con transporte **stdio** que corre en el PC del
usuario. Los conectores locales no existen en web ni en movil; es arquitectura, no
un permiso apagado.

Cuidado con una confusion facil: el repo ya tiene dos arquitecturas implementadas
y seleccionables por configuracion (ver `docs/DISENO-DESPLIEGUE.md` y el juego de
clases `IBackend` / `BackendSql` / `BackendApi`):

- **SQL directo** — el conector va derecho al SQL Server.
- **Via API** — el conector pasa por `DatamartAnalyzer.Api`.

**Ninguna de las dos resuelve el celular.** En ambas el conector sigue siendo un
proceso stdio en la maquina del usuario. Falta una tercera: publicar el **servidor
MCP mismo** sobre HTTP y registrarlo como *custom connector*, para que la conexion
salga desde los servidores de Anthropic y no desde el PC.

**Frente B — seguir evolucionando la aplicacion web** (`frontend/` + la API que la
alimenta). Ambos frentes comparten `DatamartAnalyzer.Core` y el mismo almacen de
consultas y dashboards, asi que conviene planearlos juntos y no en repos separados.

## 2. Donde esta todo (rutas verificadas)

| Que | Ruta |
|---|---|
| **Repo principal** | `C:\Proyectos\AA\datamart-analyzer` (rama `main`) |
| **Worktree de trabajo** | `C:\Proyectos\AA\datamart-mcp-remoto` (rama `feat/mcp-remoto`) |
| **MCPB generados** | `dist\` — 15 bundles, el ultimo `DatamartAnalyzer-ADPRO-win-1.5.0.mcpb` |
| **Servidor MCP** | `backend\DatamartAnalyzer.Mcp` |
| **API** | `backend\DatamartAnalyzer.Api` |
| **Logica compartida** | `backend\DatamartAnalyzer.Core` |
| **Migraciones** | `backend\DatamartAnalyzer.Migrations` |
| **Web** | `frontend\` |
| **Empaquetado** | `connector\manifest.json`, `scripts\empaquetar-conector.ps1`, `scripts\permisos-mcpb.js` |
| **SQL del almacen** | `sql\01-create-store.sql`, `sql\declarar-datamart.sql` |

**No trabajar en las copias:** existen `C:\Proyectos\AA - copia\datamart-analyzer` y
`C:\Proyectos\Migracion\Datamart\proyecto\datamart-analyzer`.

## 3. Mapa del codigo

Todo el backend es **.NET 9** (`net9.0` en los csproj).

`DatamartAnalyzer.Mcp`
- `Program.cs`, `DatamartTools.cs` — las herramientas expuestas por MCP
- `IBackend.cs`, `BackendBase.cs`, `BackendSql.cs`, `BackendApi.cs` — la
  abstraccion que permite elegir SQL directo o via API
- `ConexionDatamartDirecta.cs`, `DatamartApi.cs`

`DatamartAnalyzer.Api`
- `Program.cs`, `Controllers.cs`, `ErpController.cs`, `StoreControllers.cs`
- Servicios: `AnthropicService.cs`, `DatamartConnectionResolver.cs`,
  `ErpContextService.cs`, `SqlServerService.cs`

`frontend` — React + Vite + Tailwind
- `src\App.jsx` — **2.564 lineas en un solo archivo**
- `src\services\api.js` — 77 lineas
- `src\main.jsx`, `src\index.css`
- Git solo versiona esos cuatro archivos.

## 4. Como se levanta

`iniciar.bat` en la raiz arranca backend y frontend. Puertos fijos:

| Servicio | Puerto |
|---|---|
| API HTTP | 5000 |
| API HTTPS | 7001 |
| Frontend (Vite) | 5173 |

`vite.config.js` hace proxy de `/api` hacia `http://localhost:5000`.

**Dos advertencias practicas para trabajar en un worktree:**

1. **`npm install` es obligatorio dentro del worktree.** `frontend\node_modules`
   esta en `.gitignore` y no viaja al worktree.
2. **Los puertos son fijos y chocan.** No se puede tener `iniciar.bat` corriendo en
   el repo principal y en el worktree al mismo tiempo. O se apaga uno, o se
   parametrizan los puertos — decidir esto en el plan si se va a trabajar en
   paralelo.

`iniciar.bat` valida que exista el SDK de **.NET 8**, pero los proyectos apuntan a
**net9.0**. Revisar esa inconsistencia.

## 5. Leer antes de planear

En `docs\`: `DISENO-DESPLIEGUE.md` (las arquitecturas y la recomendacion),
`PEDIDO-INFRAESTRUCTURA.md` (el bloqueo de firewall del 10/08/2026),
`CONECTOR-COWORK.md` (almacen compartido y puente con Cowork), `CONECTOR-MAC.md`,
`INSTALAR-CONECTOR.md`, `errors-registry.md`. En la raiz: `TECHNICAL.md`, `CLAUDE.md`.

## 6. Estado del entorno

Equipo `adp-005`, Windows x64, Claude Desktop 1.44121.4, git 2.55.0,
Claude Code 2.1.228.

MCP locales activos: `Datamart ADPRO`, `sql-helpdesk`, `helpdesk-write`,
`helpdesk-rag`, `azure-devops`, `desktop-commander`.

Conexion actual del conector segun su herramienta `estado`:

```
modo:            sql
conexion:        SQL directo YOSEMITE/SincoComercial_NuevaDW
autenticacion:   Windows (integrada, sin usuario)
```

Hay ademas un datamart publicado: `datamart.sincoerp.com` (`177.253.66.218`),
puerto `4263`, con historial de bloqueo por lista blanca de IPs.

## 7. Contrato que NO debe romperse

Lectura: `estado`, `listar_datamarts`, `usar_datamart`, `describir_datamart`,
`ejecutar_sql` (solo `SELECT`/`WITH`), `preguntar`, `listar_valores_filtro`,
`fijar_filtros`, `limpiar_filtros`.

Almacen: `guardar_consulta`, `listar_consultas`, `borrar_consulta`,
`crear_dashboard`, `listar_dashboards`, `ver_dashboard`, `ejecutar_dashboard`,
`renombrar_dashboard`, `borrar_dashboard`, `agregar_panel`, `quitar_panel`, y las
de categorias y preguntas sugeridas.

Hay dashboards en uso (`Compras`, `Prueba conector`) que deben seguir funcionando,
y la web comparte ese mismo almacen.

## 8. Frentes del trabajo

**Conector remoto**
1. Exponer `DatamartAnalyzer.Mcp` sobre streamable HTTP, conservando stdio tras un flag.
2. Hosting con URL publica HTTPS que alcance el SQL Server. Evaluar reusar el host
   de `DatamartAnalyzer.Api`, que ya existe y ya tiene resuelto el tema de red.
3. Identidad: hoy hereda permisos de `SINCO\carlos` por autenticacion integrada. El
   servicio remoto necesita login propio de solo lectura, con escritura acotada al
   almacen.
4. Seguridad: OAuth en el conector, allowlist de IP de Anthropic, mantener el filtro
   de solo lectura. El datamart tiene NITs, terceros, contratos y cartera: pasa por
   seguridad de la informacion antes de publicarse.
5. Registro: el owner en *Organization settings -> Connectors*, cada persona en
   *Customize -> Connectors*.

**Aplicacion web**
6. `App.jsx` con 2.564 lineas es el cuello de botella de mantenimiento. Si se va a
   seguir construyendo ahi, modularizar primero.
7. Definir que funcionalidad nueva entra. El requerimiento concreto lo pone Carlos.

**Transversal**
8. Multi-tenant: hoy el conector trabaja contra un solo datamart, el de sus
   credenciales. Si varios clientes lo usan, la base pasa a depender del usuario
   autenticado. Decidir si entra en alcance.
9. Que pasa con el flujo `.mcpb`: se mantiene para escritorio o se deprecia cuando
   exista el conector remoto.

## 9. Que se espera del plan

Fases con cambios de codigo por archivo, el script SQL del login de servicio con sus
`GRANT`, la topologia de red con puertos y reglas de firewall, y el checklist de
registro del conector. Separar claramente lo que necesita decision de
infraestructura de lo que se puede avanzar solo con codigo. Si los dos frentes se
pueden avanzar en paralelo, decirlo; si uno bloquea al otro, decirlo tambien.
