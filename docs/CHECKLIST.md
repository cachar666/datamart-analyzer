# Checklist — conector MCP remoto y frontend

> Tracker de avance. Cada ítem dice **cómo se verifica**, no solo qué hay que hacer:
> un ítem no se marca porque alguien crea que está, se marca porque el chequeo pasa.
> Plan completo en [PLAN-MCP-REMOTO.md](PLAN-MCP-REMOTO.md).
>
> Última actualización: 06/09/2026.

**Leyenda de dueño:** 🧑 Carlos · 🤖 Claude · 🏢 Infraestructura / terceros

---

## Bloque 0 · Cerrar lo abierto

- [x] **0.1 Commitear los arreglos** — 🤖
  `9` commits locales: 6 en el conector, 2 en `ADPRO.Informes`, 1 en `ADPRO.Core.API`.
  *Verificado:* `git log --oneline origin/<rama>..HEAD` en los tres repos.

- [x] **0.1b Pushear** — 🤖 · 🧑
  Este repo tiene **dos remotos**: `origin` (mirror en Azure DevOps,
  `ADPRO.DatamartAnalyzer`) y `github` (personal, `cachar666/datamart-analyzer` — el
  que documenta `CLAUDE.md` como canónico).
  - [x] `origin` de este repo → Azure DevOps.
  - [x] `ADPRO.Informes` (`DatamartAnalyzer`) → Azure DevOps.
  - [x] `ADPRO.Core.API` (`Feature_VisualizadorDM`) → Azure DevOps.
  - [x] `github` de este repo → hecho por Carlos (login/PAT, no se maneja acá).
        *Verificado:* `git rev-list --left-right --count github/feat/mcp-remoto...HEAD` → `0 0`.

- [x] **0.2 Verificación en runtime — árbol laboratorio** — 🤖
  App corriendo contra `YOSEMITE`: sugeridas **42 ADPRO / 11 SRM**, que coincide
  exactamente con `SELECT Modulo, COUNT(*) FROM ADP_DTM_APP.SuggestedQuestions`.
  El prebuilt del gasto mensual muestra el recuadro de **7 series** y su tabla.
  *Verificado:* navegación real + consulta SQL de contraste.

- [x] **0.2b-previo El módulo quedaba en blanco en el ERP real** — 🤖
  No listado originalmente: descubierto **en vivo** (no lo vio ninguna auditoría de
  código previa) al entrar por primera vez al ERP real. Causa raíz: el shell navega
  al menú con `/Datamart` (mayúscula) y `Approuter.tsx` solo registraba `/datamart`;
  sin ruta comodín, un solo caracter de casing dejaba el módulo entero en blanco sin
  ningún error visible. Arreglado agregando la entrada exacta (`99a8375`), pusheado,
  y **desplegado dos veces** — la primera corrió sobre `5f27dfc` (viejo) porque yo
  había pusheado `ADPRO.Informes` antes de terminar los commits de esta sesión y no
  volví a pushear; detectado al leer `Source version` del build, corregido pusheando
  los 3 commits faltantes y pidiendo un segundo deploy, que sí tomó `99a8375`.
  **Confirmado por Carlos en vivo, en incógnito** (sesión real, login normal, click
  normal): el módulo carga completo, sin pantalla blanca, sin warning de rutas.

- [ ] **0.2b-conexión "No se puede conectar al servidor YOSEMITE"** — 🧑 escala a 🏢
  Carlos lo escala directo con infraestructura — queda en pendientes, no bloquea el
  resto del trabajo de este repo mientras se resuelve del otro lado.
  Encontrado **en la misma verificación de incógnito**, ya sin el problema de caché
  de por medio — no es un artefacto de prueba. El módulo carga bien (UI completa,
  pestañas Consulta/Dashboard/Sugeridas) pero al intentar conectar tira
  "No se puede conectar al servidor YOSEMITE. Verifica la red y autenticación
  Windows." **[Probable]:** es el camino `CUMBAL-V01 → YOSEMITE`, no la red en
  general — mi propio conector, corriendo con la cuenta de Windows de Carlos
  (autenticación integrada, sin usuario/clave), sí llega a YOSEMITE ahora mismo
  (`SELECT GETDATE()` en 74ms). Lo que falla es la cuenta de servicio de IIS en
  `CUMBAL-V01` (o el registro en `dbo.ADPConfig` que resuelve el datamart de esta
  tenant/proyecto), no algo que un cambio de código arregle. **Bloquea el resto de
  0.2b** (los 5 puntos de abajo necesitan datos reales). Pasar a quien administre
  permisos de SQL Server / cuentas de servicio en `CUMBAL-V01`.
  **Descartado:** no es un servidor vinculado — Carlos corrió
  `SELECT * FROM sys.servers WHERE is_linked = 1` en `cumbal-v01` y `YOSEMITE` no
  aparece en la lista (24 servidores vinculados, ninguno es YOSEMITE). Confirma que
  `ADPRO.Core.API`, corriendo en `CUMBAL-V01` (el servidor web, no el motor SQL),
  abre su propia conexión directa a YOSEMITE con la identidad de su *application
  pool* de IIS — esa es la identidad que hay que revisar.
  *Próximo paso concreto (necesita acceso a `CUMBAL-V01`):* identificar el app pool
  de `ADPRO.Core.API` en IIS, ver qué cuenta usa (`ApplicationPoolIdentity`, una
  cuenta de dominio, `NetworkService`, etc.), y probar
  `sqlcmd -S YOSEMITE -E -Q "SELECT GETDATE()"` corriendo como esa cuenta desde esa
  máquina — o de plano revisar si esa cuenta tiene login en YOSEMITE.
  **Importante, según Carlos: esto conectaba bien antes** (días/semanas atrás, no
  algo que rompió el deploy de hoy — hoy fue la primera vez que alguien llegó lo
  bastante lejos como para *ver* el intento de conexión, porque el bug de la
  pantalla en blanco lo tapaba). Es **regresión real de infraestructura**, no un
  permiso que nunca estuvo. Preguntar puntualmente: ¿rotó la clave de la cuenta de
  servicio del app pool? ¿cambió algo de red/firewall entre `CUMBAL-V01` y
  YOSEMITE? ¿se reinició o repavimentó YOSEMITE con otra config de auth?
  *Verificado con URL con cache-busting* (`?verificacion=1`, bypasea el caché del
  navegador): el bundle nuevo (`index-FgjNLkz5.js`) carga, sin el warning de rutas,
  y el módulo completo renderiza (pantalla de selección de base de datos).
  **Ojo con el pipeline de deploy:** el build de Azure DevOps (id 185) *solo*
  compila y sube el artefacto — el deploy real a `CUMBAL-V01` lo hace un **Release
  clásico** separado (`Informe SincoOk Front`, definitionId=11, deployment group
  `ADPRO-Marco-Desarrollo`), autodisparado por artefacto nuevo. Si un futuro deploy
  "no aparece", revisar ahí, no solo el build.

- [ ] **0.2b-caché `index.html` sin `Cache-Control` → sirve versión vieja tras deploy** — 🧑
  Encontrado durante la verificación de arriba: IIS no manda `Cache-Control` en
  `index.html` de `Informe`, así que el navegador cachea heurísticamente esa URL
  exacta (sin query string) y **un deploy nuevo no la invalida** — confirmado con
  el Performance API dentro del iframe real: `type: "navigate"` pero
  `transferSize: 0` (servido 100% desde caché), incluso en pestañas nuevas o con
  `Ctrl+Shift+R` del documento padre (el `iframe` del shell no forma parte de esa
  recarga). Se repetirá en **cada deploy futuro**, para cada navegador que ya haya
  visitado el módulo antes de ese deploy.
  - [ ] **Workaround inmediato** (para verificar 0.2b hoy): abrir el ERP en una
        ventana de incógnito, o DevTools → Network → "Disable cache" → recargar.
  - [ ] **Arreglo permanente**: `Cache-Control: no-cache` en `index.html` (dejar
        `assets/*.js`/`*.css`, con hash en el nombre, cacheables para siempre como
        ya están). Va en el `web.config` de `Informe` — no lo toco sin que alguien
        del equipo del ERP confirme dónde vive ese `web.config` en `CUMBAL-V01`.

- [ ] **0.2b Verificación en runtime — árbol del ERP** — 🧑
  **No se pudo hacer acá:** ese árbol necesita `ADPRO.Core.API` con su configuración,
  sus tokens y `dbo.ADPConfig`. La lógica equivalente sí se probó en el laboratorio.
  Bloqueado ahora mismo por el ítem de caché de arriba: sin incógnito o sin
  "Disable cache", la UI real sigue mostrando el bundle viejo. Pasada manual
  pendiente, en este orden:
  - [ ] Vista de sugeridas: cargan las 42 de ADPRO; la pestaña SRM muestra 11; ninguna
        tarjeta rompe (antes: lista vacía y `TypeError` en el render).
  - [ ] Una pregunta que pida **tabla + interpretación**: se ve la tabla *y* la prosa
        (es lo que el typo del enum rompía).
  - [ ] Un prebuilt con agrupación: recuadro de N series + tabla, no una línea saltando.
  - [ ] Reejecutar una consulta guardada cuyo SQL falle: error **sin** el "No se
        encontraron registros" debajo.
  - [ ] Lista de consultas guardadas: fecha legible, no "Invalid Date".
        *Ojo:* el almacén tiene **0 consultas guardadas**, así que hay que crear una —
        **a mano, desde la UI real**, no vía el conector: `guardar_consulta` es
        visible en la app web y su propia descripción dice explícitamente no usarlo
        por iniciativa propia ("el listado es del usuario, se llenaría de ruido").
  - [ ] En Dashboard con un filtro de proyecto aplicado, cambiar de datamart: los
        paneles del nuevo **no** arrastran el filtro. Requiere un segundo datamart
        que también tenga dashboards; hoy solo los tiene `SincoComercial_NuevaDW`.

- [ ] **0.1c Abrir PR en los dos repos del ERP** — 🧑
  Después de 0.2b. Commitear sin haberlo visto correr es aceptable; pedir revisión, no.

- [x] **0.3 Back-port al laboratorio** — 🤖
  5 de los 6 arreglos también faltaban en `frontend/src/App.jsx`. Portados.
  *Verificado:* `npm run build` en verde + la navegación de 0.2.

---

## Bloque 1 · El hueco de verificación

- [x] **1.1 Diagnosticar por qué el build no vio el bug** — 🤖
  La config de ESLint solo matchea `files: ['**/*.{ts,tsx}']`; el módulo datamart es
  el único JavaScript del repo (3 archivos contra 139 `.ts/.tsx`), así que **nunca
  entraba**. Probé agregarlo y **lo reverti**: sin `eslint-plugin-react` produce 67
  errores falsos, porque el uso dentro de JSX no cuenta como referencia.

- [x] **1.2 Corregir la afirmación del plan** — 🤖
  El lint **no** habría atrapado el bug de `SuggestionCard`: un prop que el
  componente no declara no es un error en JavaScript. Lo que lo cierra es TypeScript.

- [ ] **1.3 Cerrarlo con TypeScript, al modularizar** — 🤖 *(decidido, ver bloque 7)*
  Cada pieza que se extraiga sale `.tsx` con props tipados, y el contrato lo verifica
  el `tsc -b` que ya corre.
  *Cómo se verifica:* pasar un prop que el componente no declara **rompe el build**.

---

## Bloque 2 · Correcciones que cruzan repos

- [x] **2.1 Typo del enum en `ADPRO.Core.API`** — 🤖
  `MapDatamartAnalyzerRoutes.cs`: `"tablaSmastexto"` nunca coincidía, así que toda
  respuesta tabla+interpretación caía a `Texto` y el front no dibujaba la tabla.
  *Verificado:* compila. **Falta el chequeo funcional de 0.2b.**

- [x] **2.2 La tabla como rama por defecto** — 🤖
  En vez de lista blanca de tipos. El cambio es **monótono en la dirección segura**:
  el conjunto viejo `{Tabla, TablaMasGrafico, TablaMasTexto}` es subconjunto estricto
  del nuevo `{todo salvo Error y Grafico}`, así que ningún tipo que antes pintaba
  tabla dejó de pintarla. Aplicado en `MessageBubble` y en el `showTable` del panel,
  en los dos árboles.

- [x] **2.4 Guard de `StoreRepository` extendido a `Texto`** — 🤖
  En las **dos** copias que compilan. La copia vendorizada en `datamart-analyzer/`
  del worktree del ERP no se tocó: está versionada pero ningún `.csproj` ni `.sln`
  la referencia — es un volcado de referencia.

- [ ] **2.3 SQL con filtros cocinados — el arreglo de fondo** — 🧑 decide, 🤖 ejecuta
  Lo hecho es la **mitigación**: un panel de SQL generada por IA guardado con filtros
  activos ya no declara que acepta los globales (evita
  `IN ('NORTE') AND IN ('SUR')` → cero filas mostradas como "sin resultados").
  El arreglo real necesita que el backend devuelva la SQL **sin** filtros (`SqlBase`,
  que hoy duplica `Sql`) y que el frontend la mande. Cruza los dos repos y las dos
  copias de `Core`.
  *Cómo se verifica:* guardar un panel con filtros activos, quitarlos en el dashboard,
  y que el panel muestre el total sin filtrar.

---

## Bloque 3 · Datos y empaquetado

- [x] **3.1 Reparar los paneles con gráfico prometido y `GraficoJson` NULL** — 🤖
  4 paneles corregidos a `Tabla`, en transacción con guarda de "solo commitea si toca
  exactamente 4". El de *Evolución mensual de compras* estaba **vacío**: lo medí en el
  DOM (286 px de alto, cero caracteres, sin tabla y sin SVG).
  *Verificado:* `SELECT COUNT(*) ... WHERE GraficoJson IS NULL AND TipoRespuesta IN
  ('Grafico','TablaMasGrafico')` → **0**.

- [x] **3.2 Reempaquetar el `.mcpb`** — 🤖 empaquetado y verificado · 🧑 falta instalar
  **Era la causa raíz de 3.1**, y el instalado (1.5.0, 11/08 12:22) sigue vivo hasta
  que lo reemplaces: el guard entró a las 13:01 de ese mismo día, 39 minutos después.
  - [x] `manifest.json` con las 7 herramientas que faltaban — declaraba 21, ahora 28.
  - [x] `scripts\empaquetar-conector.ps1 -Plataforma win -Version 1.6.0` → generó
        `dist\DatamartAnalyzer-ADPRO-win-1.6.0.mcpb` (37.2 MB).
  - [x] Verificado *dentro* del `.mcpb`: `manifest.json` trae **28** tools y
        `version 1.6.0`; el `.exe` empaquetado es del 04/09, muy posterior al commit
        del guard — el binario instalado sí lo tiene.
  - [ ] **Instalarlo vos**: Claude Desktop → Configuración → Extensiones → **apagar
        la actual antes** (si no, falla con `EPERM` porque el proceso retiene
        `coreclr.dll`) → arrastrar el `.mcpb` nuevo.
  *Cómo se verifica ya instalado:* `estado` reporta la extensión nueva; `agregar_panel`
  con `tipoRespuesta=Grafico` y sin ejes queda guardado como `Tabla`.

---

## Bloque 4 · Documentación

- [x] **4.1 `CLAUDE.md` del workspace** — 🤖
  Pendientes reescritos (los dos que estaban ya se cerraron), y la trampa de
  `pnpm build` corregida: decía "correr `pnpm install` primero" y eso **da el mismo
  error** con `node_modules` presente. Lo que hay que saltear es el chequeo previo de
  dependencias: `./node_modules/.bin/tsc -b && ./node_modules/.bin/vite build`.

- [x] **4.2 `iniciar.bat`** — 🤖 el mensaje pedía el SDK de .NET 8; los proyectos son `net9.0`.

- [x] **4.3 `CLAUDE.md` del módulo del ERP** — 🧑 aportó, 🤖 escribió
  Reescrito completo. Verificado por código (agente `Explore`): rutas reales
  bajo `api/datamart/*` (no `/api/analyze` plano), stack real (Vite puerto
  3600, backend net8.0 minimal API + un `ICarterModule`), DI (`IConexionDatamart`
  Singleton vía `dbo.ADPConfig`, resto Scoped), autorización anónima a nivel
  HTTP, y los TODOs que ya estaban resueltos (modo oscuro, favoritos, historial
  persistente, caché de schema). Carlos aportó lo que el código no dice: el
  flujo real de arranque (mixto, típicamente los dos locales) y que la falta
  de `[Authorize]` es deliberada (protegido por vivir embebido en el shell del
  ERP + red interna, no un gap). Pendientes reales que quedaron anotados:
  exportar a Excel, pivote de series múltiples en `ChartDisplay`.
  *Verificado:* diff revisado por Carlos antes de commitear.

- [x] **4.4 `TECHNICAL.md` del módulo** — 🤖
  Afirmaba que las consultas se guardan en `localStorage` (lo que justo se migró),
  en tres lugares: la tabla de estados (`savedQueries`), y las listas de "qué
  hace"/"qué no hace" el sistema. Las tres corregidas: viven en el almacén del
  datamart (`ADP_DTM_APP`), visibles también desde el conector MCP.
  *Verificado:* commit `111a58a` en `ADPRO.Informes`.

---

## Bloque 5 · Publicar el conector remoto

- [x] **5.0 Host HTTP con OAuth propio, funcionando en `127.0.0.1`** — 🤖
  28 herramientas en Production, 401 sin token, PKCE S256, código de un solo uso,
  refresh con rotación, credenciales cifradas con AES-256-GCM, estado de sesión por
  identidad de credencial que sobrevive al refresco del token.

- [x] **5.5 Segundo camino: integrado en `ADPRO.Core.API`** — 🤖 (decisión de Carlos)
  5.0 es el host standalone de este repo (`datamart-mcp-remoto`), que sigue
  esperando 5.3. Carlos decidió **no esperarlo**: construir el mismo protocolo
  MCP + su servidor OAuth **dentro de `ADPRO.Core.API`** (repo `ADPRO-Datamart`,
  rama `Feature_VisualizadorDM`), reusando el servidor donde esa API ya corre —
  sin pedir infraestructura nueva. Explícitamente pensado para poder sacarlo a su
  propio pool/servidor después (net10 u otro): por eso vive en su **propio
  proyecto**, `Features/DatamartMcp`, y no mezclado en `API/` ni en
  `Features/DatamartAnalyzer`.
  - [x] Identidad: **igual que el `.mcpb`** — servidor/base/usuario/clave SQL en
        la pantalla de OAuth, no el usuario del ERP. Decidido explícitamente por
        Carlos tras plantear la alternativa (login del ERP + `dbo.ADPConfig`).
  - [x] Servidor OAuth 2.1 completo portado: PKCE S256, código de un solo uso,
        refresh con rotación, credenciales cifradas AES-256-GCM. Mismo diseño que
        5.0, adaptado de namespace.
  - [x] **El problema real que esto encontró**: las clases de negocio
        (`SqlExecutor`, `SchemaService`, `StoreRepository`) resuelven su conexión
        vía `IConexionDatamart`, registrado como **Singleton** por `dbo.ADPConfig`
        para las rutas REST existentes. Las tools MCP necesitan una conexión **por
        request**, armada de la credencial de la sesión OAuth — no se puede
        re-registrar la misma interfaz sin arriesgar romper las rutas REST.
        Resuelto sin tocar el registro existente: las tools construyen
        `SqlExecutor`/`SchemaService`/`StoreRepository` **a mano** (`new`, no DI),
        pasándoles un `ConexionFija` armado de la credencial — esas clases ya
        aceptan `IConexionDatamart` + logger en su constructor y no dependen de
        nada más del contenedor.
  - [x] **Las 28 tools, catálogo completo** — mismos nombres y firmas que
        `DatamartAnalyzer.Mcp` (el conector standalone), delegando en los
        servicios de `Features.DatamartAnalyzer` en vez de reimplementar. Incluye
        filtros globales de conversación (`fijar_filtros`/`limpiar_filtros`),
        con el mismo diseño de estado-fuera-del-scope-de-DI que 5.0
        (`EstadoDeSesionesMcp`, indexado por identidad de credencial, caduca por
        inactividad). `listar_valores_filtro` reusa `Sql.FilterCatalog` — la
        misma tabla proyecto/macroproyecto/empresa/estado que ya usan las rutas
        REST, no una copia.
        **Simplificación consciente**: la versión REST de `listar_valores_filtro`
        también sabe enriquecer proyecto/macroproyecto con empresa y descripción
        (`empresaPorValor`, `metadataPorValor`) y tiene un fallback de
        descubrimiento dinámico por esquema para tipos no mapeados; la tool MCP
        devuelve solo los valores distintos de los 4 tipos conocidos. Ninguna
        tool de escritura queda sin la guarda de "no crear por iniciativa propia"
        que ya tenía el conector standalone (`agregar_pregunta_sugerida`,
        `guardar_consulta`, etc., en la descripción que lee Claude).
        *Verificado:* `dotnet build API/ADPRO.Core.csproj` en verde, 0 errores,
        con las 28 `[McpServerTool]` presentes.
  - [x] Tabla de tokens: `dbo.AlmacenAutorizacionMcp` en la base **`Sinco` de
        `cumbal-v01`** (la misma que ya usa `ErpContextService` para leer
        `dbo.ADPConfig`) — NO una base nueva. Script en
        `Features/DatamartMcp/sql/AlmacenAutorizacionMcp.sql`, sin correr todavía
        (`CREATE TABLE` real, necesita aprobación explícita antes de ejecutarse).
  - [x] Endpoint MCP en `/mcp`, `RequireAuthorization` con el esquema
        `TokenOpacoMcp` — no el default de la app (hoy no hay ninguno registrado,
        confirmado por exploración: cero `[Authorize]` en todo el repo). OAuth
        (`/authorize`, `/token`, `/register`, `/.well-known/oauth-authorization-server`)
        en la raíz, verificado libre.
  - [x] **`dotnet build API/ADPRO.Core.csproj` en verde — 0 errores.**
  - [x] **Tabla creada** — Carlos corrió el script contra `Sinco`/`cumbal-v01`
        (yo no pude: el clasificador de modo automático bloqueó `sqlcmd` contra
        una base de producción, ni siquiera con aprobación explícita en el chat —
        correcto, no lo intenté rodear). Verificado por Carlos: 9 columnas, PK
        clustered sobre `IndiceHash`, índice `IX_AlmacenAutorizacionMcp_Expira`.
        Con `appConnection` ya declarada en `appsettings.json` apuntando a
        `cumbal-v01`/`Sinco`, el almacén de tokens pasa a SQL automáticamente sin
        config nueva — `AddDatamartMcp` ya cae a `ErpConnection ?? appConnection`.
  - [x] **Desplegado a `CUMBAL-V01`** — build `54463` (Core SincoOk, definitionId
        187) en verde sobre `Feature_VisualizadorDM`/`425eabb6`; Release-206
        (`Core SincoOk`) se disparó solo por el artefacto nuevo — 1/1 target,
        0 fallos. Ojo: la primera corrida del pipeline (`54458`) compiló
        `xkala-backend` por error — el selector de rama no se cambió la primera
        vez; corregido pidiendo una segunda corrida con la rama explícita.
  - [x] **Bug real encontrado y arreglado en vivo**: el primer intento de login
        real (YOSEMITE/SincoComercial_NuevaDW) tiró
        `SqlException`/`Win32Exception`: "La cadena de certificación fue emitida
        por una entidad en la que no se confía", en
        `AlmacenDeAutorizacionEnSql.Abrir()`. **No** era la conexión al datamart
        (esa ya había validado bien, el código llegó a `EmitirCodigo`): era la
        conexión al almacén de tokens en `cumbal-v01`/`Sinco`, armada con
        `appConnection`/`ErpConnection` **sin** `TrustServerCertificate`.
        Arreglado en `RegisterInterface.AddDatamartMcp`: la cadena se pasa por
        `SqlConnectionStringBuilder` forzando `TrustServerCertificate = true`
        antes de dárselo a `AlmacenDeAutorizacionEnSql` — mismo motivo por el
        que ya lo llevan las conexiones a datamarts de cliente.
        *Verificado:* reintentado el login tras el fix, código emitido sin error.
  - [x] **`DATAMART_MCP_LLAVE_CIFRADO` en local** — puesta con `setx` (variable de
        usuario de Windows, no versionada) para la prueba de hoy. **Sigue
        pendiente en `CUMBAL-V01`** (IIS Application Pool → Environment Variables,
        + reciclar el pool) antes de desplegar ahí.
  - [x] **Hallazgo real, ya corregido**: "Usuario vacío = autenticación de Windows"
        tenía sentido en el `.mcpb` local (el proceso corre bajo la sesión de
        Windows de esa persona) pero **no** en este host compartido — ahí "vacío"
        autentica como la identidad de Windows del *proceso del servidor* (hoy la
        sesión de quien lo prueba en local; mañana el application pool de IIS), no
        la de cada usuario remoto. Dos personas distintas dejando el usuario vacío
        terminarían conectadas con la MISMA identidad, rompiendo el aislamiento por
        credencial que es el punto de todo esto.
        **Arreglado en `EndpointsDeAutorizacion.ProcesarFormulario`**: usuario y
        clave son obligatorios ahora — error explícito si se dejan vacíos, sin caer
        a `IntegratedSecurity`. El formulario ya no insinúa la opción de Windows.
        *Verificado:* `dotnet build API/ADPRO.Core.csproj` en verde, 0 errores.
        **Falta probarlo en vivo** (reiniciar el servidor local y confirmar que
        dejar usuario/clave vacíos ahora devuelve el error, no un login exitoso).
  - [x] **Flujo completo probado en vivo, de punta a punta** — 🤖
        Login OAuth real contra `http://localhost:5250/authorize` (PKCE S256,
        credencial YOSEMITE/SincoComercial_NuevaDW con usuario/clave vacíos →
        autenticación de Windows), canje en `/token` con `code_verifier`
        correcto → `access_token` + `refresh_token`. Con ese token contra
        `/mcp`:
        - `initialize` → handshake ok, `serverInfo.name = adpro-datamart-analyzer`.
        - `tools/list` → **28** herramientas, mismos nombres/esquemas que el
          conector standalone.
        - `estado` → `{"datamartActivo":"SincoComercial_NuevaDW","conexion":
          "YOSEMITE/SincoComercial_NuevaDW","almacen":{"alcanzable":true,
          "instalado":true}}` — confirma que `DatamartMcpContext` arma la
          conexión per-request desde la credencial de OAuth, no desde el
          `IConexionDatamart` Singleton del modo ERP.
        - `ejecutar_sql` (`SELECT TOP 3 name FROM sys.tables`) → 3 filas reales.
        - `listar_dashboards` → los dos dashboards reales del datamart
          (`Compras`, `Prueba conector`), con sus metadatos exactos.
        *Nota de proceso:* dos intentos de canje fallaron primero por
        transcribir a mano el `code` de una captura de pantalla (el hash no
        coincidía con el guardado — probable confusión de caracteres `_`/`-`/`l`/`1`
        en la imagen); resuelto haciendo todo el flujo — llenar el formulario,
        submit, leer el `code` real — desde el Browser pane, sin transcripción
        manual.
  - [ ] Pendiente: comitear — todo esto sigue sin commit en el worktree.
  - [ ] Antes de `CUMBAL-V01`: sacar la opción de usuario/clave vacíos (ver
        hallazgo de arriba) y poner `DATAMART_MCP_LLAVE_CIFRADO` en el entorno
        real.

- [ ] **5.3 Pedido a infraestructura** — 🏢 *(arrancar YA: mayor tiempo de espera)*
  *(esto sigue siendo para 5.0, el host standalone — 5.5 no lo necesita)*
  - [ ] Regla 1: IPs de Anthropic (**`160.79.104.0/21`** IPv4, **`2607:6bc0::/48`**
        IPv6 — outbound, ver 5.2) → `mcp-datamart.sincoerp.com` **443/TCP**, con
        certificado TLS válido y proxy inverso.
  - [ ] Regla 2: IP del host central → SQL de cada datamart, **4263 o 1433**.
  - [ ] El proceso como servicio de Windows.
  - [ ] **Revisión de seguridad de la información**, con la custodia de credenciales
        de clientes declarada explícitamente: el radio de daño de una brecha del host
        central pasa a ser *todos los datamarts*.
  *Límite honesto:* cada cliente con datamart propio sigue teniendo que autorizar
  nuestra IP. Es **una** IP en vez de una por sede, pero no es cero gestión.

- [ ] **5.1 Lo que falta de código** — 🤖
  - [x] `Descripcion` del backend por sesión — **ya lo era**, no hacía falta código
        nuevo. Reconstruí la cadena de DI: `BackendConEstadoPorToken` → `BackendSql`
        → `ConexionDatamartDirecta` son las tres **Scoped**, y esta última se arma en
        cada scope desde `CredencialDeLaSolicitud.De(httpContext)` — la credencial
        *de ese request*, no de un caché compartido. El spike de A1 ya midió que un
        scope nuevo nace en cada tool call, así que no hay estado congelado posible.
        *Sin verificar con un segundo datamart real* (solo tengo `YOSEMITE` a mano);
        si aparece un segundo datamart de prueba, repetir el chequeo de dos sesiones
        de A2 pero comparando el campo `conexion` de `estado`, no solo los filtros.
  - [x] **Almacén de tokens** — 🤖 implementado, 🧑 decidió SQL sobre Redis (ya se
        opera y respalda). `AlmacenDeAutorizacionEnSql` (misma interfaz, mismo
        contrato que la version en memoria: canje de código y rotación de refresh
        atómicos vía `DELETE ... OUTPUT`, tokens guardados hasheados). Esquema en
        `sql/02-create-oauth-store.sql`, dentro de la base `DatamartAnalyzer` que ya
        existe en YOSEMITE (no una base nueva — es estado del host, no de un
        cliente). `Program.cs` usa SQL si `BaseDeHost:CadenaConexion` está puesta,
        y cae a memoria si no (desarrollo local sin depender de SQL).
        *Verificado:* `dotnet build` en verde, 0 advertencias.
        **Falta, y no lo hago sin aprobación explícita — es un `CREATE TABLE`
        real:** correr `sql/02-create-oauth-store.sql` contra YOSEMITE, y poner
        `BaseDeHost:CadenaConexion` en el entorno donde termine viviendo el host
        (no versionado en claro, mismo criterio que `DATAMART_LLAVE_CIFRADO`).
  - [x] **Revisado** `TrustServerCertificate = true` (`Core/Models/DatamartModels.cs:44`) — 🤖
        **[Seguro]** Se aplica sin condición, a las dos rutas (SQL directo local y,
        mañana, el host central hacia datamarts de clientes). Encriptación TLS sigue
        activa (`Encrypt` es `true` por defecto en `Microsoft.Data.SqlClient` desde
        hace varias versiones) — lo que se salta es la validación de la cadena de
        certificado, no el cifrado del canal.
        **[Probable] Hoy no es un problema real:** los datamarts SQL Server on-prem
        casi siempre traen certificado autofirmado; exigirle a cada cliente un
        certificado firmado por una CA pública para conectar es poco realista.
        **Si importa antes de publicar el conector remoto (bloque 5):** ahí el host
        central sale a internet hacia el SQL de cada cliente. Sin validar cadena,
        un atacante en el camino puede interceptar la conexión sin que nada lo
        note. Mitiga bastante que el modelo de 5.3 ya es punto a punto por IP fija
        (no internet abierto) — no lo elimina. **No cambio código sin que decidan**
        entre: (a) dejarlo así apoyado en el firewall de 5.3, (b) fijar el
        *thumbprint* esperado por datamart en vez de confiar a ciegas, o (c) exigir
        certificado de CA pública como requisito de alta de cliente.
  - [ ] Kestrel fuera de `127.0.0.1`, **solo** detrás del proxy y con 5.3 listo.

- [x] **5.2 Verificar contra la documentación de desarrollador** — 🤖
  - [x] ¿Claude exige `/register`? **No.** El Owner puede cargar un client id/secret
        estáticos al registrar el conector; el registro dinámico (RFC 7591) es
        opcional. El nuestro ya está implementado y anda — se deja: no hace daño y
        cubre a cualquier otro cliente MCP que sí lo use por convención.
  - [x] Rangos de IP de Anthropic para la allowlist — **segunda** capa, nunca la
        primera. Publicados en `platform.claude.com/docs/en/api/ip-addresses`
        (no en la de soporte, ahí es donde no aparecían):
        **outbound IPv4 `160.79.104.0/21`**, **IPv6 `2607:6bc0::/48`** — este es
        el rango que hace las llamadas de MCP tool calls hacia servidores
        externos, el que aplica a la regla 1 de 5.3. La página dice
        "will not change without notice" — estable, pero sin garantía de por
        vida; revisar antes de fijarlo en un firewall que no se revisa seguido.
        **Hallazgo aparte, no aplica acá:** existe "MCP tunnels" (investigación
        preliminar, acceso por invitación) que evita el allowlisting por completo
        con un túnel saliente — pero la propia documentación dice explícitamente
        que **"MCP tunnels created through the Console are not available as
        connectors in claude.ai"**: solo sirve para Managed Agents / Messages
        API, no para el registro de conector en *Organization settings →
        Connectors* que necesita 5.4. No cambia el plan de 5.3.

- [x] **5.2b Cómo lo encuentran los usuarios** — 🤖 investigado, verificado en la
      documentación de Anthropic. **No** es "cada persona busca una URL": en
      Team/Enterprise el Owner registra el conector **una sola vez**
      (*Organization settings → Connectors*, solo pide la URL — no hay campo de
      nombre) y cada persona después lo ve listado en *Customize → Connectors* sin
      escribir nada, solo hace clic en "Connect".
      **Límite real:** sin coincidencia en el *Connectors Directory* de Anthropic
      (curado por ellos, no autoregistrable — el ejemplo que dan es un partner ya
      integrado), la lista muestra la etiqueta genérica **"Custom"**, no
      "ADPRO Datamart Analyzer". Fijé `ServerInfo.Title` en el protocolo MCP a ese
      nombre (`e33bb18`) porque es la única palanca de código que tenemos, pero no
      hay evidencia de que Claude lo use como etiqueta visible — **verificar viendo
      el registro real**, no antes.

- [ ] **5.4 Registrar el conector** — 🧑 (Owner de la organización)
  - [ ] *Organization settings → Connectors*, con client id/secret.
  - [ ] Sin token → **401** desde fuera de la red.
  - [ ] Con token → `tools/list` devuelve las **28** con los nombres intactos.
  - [ ] Dos sesiones con credenciales distintas **no** se cruzan (ni conexión, ni
        esquema cacheado, ni filtros).
  - [ ] `Compras` y `Prueba conector` se ven desde el remoto.
  - [ ] **Confirmar qué etiqueta muestra realmente la lista** — "Custom" o el
        nombre del protocolo — y anotarlo, ya que 5.2b quedó sin verificar.

---

## Bloque 6 · Deuda de la auditoría

Confirmados por refutación adversarial, arreglados y **revisados por un segundo
agente adversarial** antes de commitear (`86bb44c`). Todos en
`DatamartAnalyzer.jsx` salvo donde se indique. — 🤖

- [x] `~295` El eje Y convierte `NULL` en 0 → ahora `null` (hueco en la serie), con
      aviso de cuántas filas quedaron sin valor.
- [x] `~1663` Detalle de dashboard que falla en cargar → distinguido de "vacío de
      verdad" (`errorCarga`, verificado que `d.totalPaneles` llega poblado desde
      `StoreModels.cs:62` + `StoreRepository.cs:353`), con botón Reintentar y aviso
      si vuelve a fallar.
- [x] `~1956` `agregarPanel` descartaba el resultado → capturado. **El review
      encontró que este arreglo, tal como se hizo primero, abría un doble-envío**
      (el modal quedaba abierto e interactivo durante el `await`, sin guarda de
      reentrancia): doble clic → panel duplicado; reintento tras fallo parcial →
      dashboard huérfano; "Cancelar" no cancelaba la escritura en curso. Corregido:
      reentrancia con un `ref` síncrono, el dashboard creado se reutiliza en el
      reintento, y Cancelar/backdrop/Escape no cierran mientras hay una escritura
      en vuelo.
- [x] `~1770` El `catch` de `handleSelectDb` esperaba una forma de error de axios
      que `fetchApi` nunca produce → chequeo explícito de `undefined`.
- [x] `~1778` Paneles de la base anterior reejecutándose contra la nueva → se vacía
      `dashboards`/`savedQueries` antes del `await`, no solo después.
- [x] `~739` `resp.tipoRespuesta` sin guarda → guard en las 3 ramas de
      `DashboardPanel.ejecutar` y en las 2 de `handleSend`.
- [x] `~1946` Comentario que afirmaba una validación server-side del gráfico que no
      existe → corregido.
- [~] `~24` Selector de tema inerte → **solo la mitad segura.** `MessageBubble` ya
      no llama su propio `useTheme()` (recibe el tema por prop: antes dos copias
      independientes competían por el mismo `localStorage`/`classList`, y una
      burbuja ya montada no seguía un cambio de tema en vivo). **Sigue sin decidir**
      si se escriben las reglas CSS `.theme-day`/`.theme-night` que nunca
      existieron, o se retira el toggle del header — no es una decisión de código,
      necesita definir cómo se ven esos temas o si se abandonan.

- [ ] **~1934** Consulta guardada persiste SQL con filtros, se reejecuta sin
      mencionarlos. **No tocado en esta pasada** — depende de **2.3** (cruza repos).

**Descartado tras verificar** — "borrar una consulta recién guardada no la borra del
almacén": `handleSaveQuery` **sí** reemplaza el id optimista por el del servidor. Es
una ventana de carrera estrecha, no un bug permanente.

**Opcional, es una función, no un arreglo** — el pivote de series múltiples en
`ChartDisplay`: una columna por grupo, una serie por columna, `<Legend>` real. Lo hecho
es el fallback honesto. **9 prebuilts** lo aprovecharían.

---

## Bloque 7 · Modularizar `App.jsx` + conversión a TypeScript

- [ ] **7.1** Corte por costura existente: `components/`, `filters/`, `dashboard/`,
      `hooks/`, `lib/`. Ya hay 15 componentes separados; `App` en sí son ~1.200 de las
      ~2.900 líneas. — 🤖
- [ ] **7.2** Cada pieza extraída sale **`.tsx`** con props tipados. Esto es el
      bloque 1.3: el contrato lo verifica el `tsc -b` que ya corre. — 🤖
- [ ] **7.3** El nudo real: ~40 `useState` y una docena de `useRef` que solo existen
      para que los callbacks vean el valor fresco. Extraerlos a un reducer **se decide
      aparte**, no se esconde dentro de "modularizar". — 🧑 decide

---

## Orden sugerido

```
AHORA        0.1b push  ·  5.3 pedido de infra (arranca el reloj mas largo)
LUEGO        0.2b pasada manual del ERP  ->  0.1c PR
EN PARALELO  3.2 reempaquetar  ·  4.3+4.4 docs  ·  6 deuda
CUANDO       5.1 + 5.2  ->  5.4 registrar          <- necesita 5.3
AL FINAL     2.3 (cruza repos)  ·  7 modularizar + TypeScript
```

**Lo que bloquea a qué:** 0.2b bloquea el PR. 5.3 bloquea todo el bloque 5 y es lo de
mayor espera — por eso va primero aunque no sea urgente. 3.2 es independiente y su
causa raíz **sigue escribiendo datos malos** cada vez que se agrega un panel desde la
extensión instalada. El bloque 7 debería ir después de 0.2b, para no modularizar
encima de algo que todavía no se vio funcionar.
