# Checklist — conector MCP remoto y frontend

> Tracker de avance. Cada ítem dice **cómo se verifica**, no solo qué hay que hacer:
> un ítem no se marca porque alguien crea que está, se marca porque el chequeo pasa.
> Plan completo en [PLAN-MCP-REMOTO.md](PLAN-MCP-REMOTO.md).
>
> Última actualización: 03/09/2026.

**Leyenda de dueño:** 🧑 Carlos · 🤖 Claude · 🏢 Infraestructura / terceros

---

## Bloque 0 · Cerrar lo abierto

- [x] **0.1 Commitear los arreglos** — 🤖
  `9` commits locales: 6 en el conector, 2 en `ADPRO.Informes`, 1 en `ADPRO.Core.API`.
  *Verificado:* `git log --oneline origin/<rama>..HEAD` en los tres repos.

- [ ] **0.1b Pushear** — 🧑
  Ninguno subido. El conector va a GitHub personal y **pide un PAT**, no contraseña.
  Los dos del ERP van a Azure DevOps.
  *Cómo se verifica:* `git log origin/<rama>..HEAD` devuelve vacío en los tres.

- [x] **0.2 Verificación en runtime — árbol laboratorio** — 🤖
  App corriendo contra `YOSEMITE`: sugeridas **42 ADPRO / 11 SRM**, que coincide
  exactamente con `SELECT Modulo, COUNT(*) FROM ADP_DTM_APP.SuggestedQuestions`.
  El prebuilt del gasto mensual muestra el recuadro de **7 series** y su tabla.
  *Verificado:* navegación real + consulta SQL de contraste.

- [ ] **0.2b Verificación en runtime — árbol del ERP** — 🧑
  **No se pudo hacer acá:** ese árbol necesita `ADPRO.Core.API` con su configuración,
  sus tokens y `dbo.ADPConfig`. La lógica equivalente sí se probó en el laboratorio.
  Pasada manual pendiente, en este orden:
  - [ ] Vista de sugeridas: cargan las 42 de ADPRO; la pestaña SRM muestra 11; ninguna
        tarjeta rompe (antes: lista vacía y `TypeError` en el render).
  - [ ] Una pregunta que pida **tabla + interpretación**: se ve la tabla *y* la prosa
        (es lo que el typo del enum rompía).
  - [ ] Un prebuilt con agrupación: recuadro de N series + tabla, no una línea saltando.
  - [ ] Reejecutar una consulta guardada cuyo SQL falle: error **sin** el "No se
        encontraron registros" debajo.
  - [ ] Lista de consultas guardadas: fecha legible, no "Invalid Date".
        *Ojo:* el almacén tiene **0 consultas guardadas**, así que hay que crear una.
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

- [ ] **3.2 Reempaquetar el `.mcpb`** — 🧑
  **Es la causa raíz de 3.1 y sigue viva:** el instalado (1.5.0) se armó el 11/08 a
  las **12:22** y el guard que impide ese dato entró a las **13:01**, 39 minutos
  después. Cualquier panel que agregues hoy desde esa extensión reproduce el defecto.
  - [ ] `powershell -ExecutionPolicy Bypass -File scripts\empaquetar-conector.ps1 -Plataforma win -Version 1.6.0`
  - [ ] Agregar al `manifest.json` las **7** herramientas que le faltan (declara 21,
        el código expone 28: las de sugeridas y categorías).
  - [ ] Apagar la extensión antes de reinstalar, o falla con `EPERM`.
  *Cómo se verifica:* desde el `.mcpb` nuevo, `agregar_panel` con
  `tipoRespuesta=Grafico` y sin ejes queda guardado como `Tabla`; y `tools/list`
  devuelve 28 nombres.

---

## Bloque 4 · Documentación

- [x] **4.1 `CLAUDE.md` del workspace** — 🤖
  Pendientes reescritos (los dos que estaban ya se cerraron), y la trampa de
  `pnpm build` corregida: decía "correr `pnpm install` primero" y eso **da el mismo
  error** con `node_modules` presente. Lo que hay que saltear es el chequeo previo de
  dependencias: `./node_modules/.bin/tsc -b && ./node_modules/.bin/vite build`.

- [x] **4.2 `iniciar.bat`** — 🤖 el mensaje pedía el SDK de .NET 8; los proyectos son `net9.0`.

- [ ] **4.3 `CLAUDE.md` del módulo del ERP** — 🧑 aporta, 🤖 escribe
  `ADPRO.Informes/src/pages/datamart/CLAUDE.md` publica endpoints equivocados
  (`/api/analyze` cuando el cliente real usa `api/datamart/analyze` vía
  `providerHTTP.fetchApi`), describe el arranque del laboratorio (`dotnet run` +
  `npm run dev`, que en ese árbol no existe), y su TODO lista "Modo oscuro" y
  "Favoritos" como pendientes cuando los dos existen. **Se carga como instrucciones
  del proyecto**, así que desvía a quien trabaje ahí.
  **No lo escribo inventando** el flujo real de arranque de ese módulo: con dos líneas
  tuyas lo dejo correcto.

- [ ] **4.4 `TECHNICAL.md:336` del módulo** — 🤖
  Afirma que las consultas se guardan en `localStorage`, que es justo lo que se migró.

---

## Bloque 5 · Publicar el conector remoto

- [x] **5.0 Host HTTP con OAuth propio, funcionando en `127.0.0.1`** — 🤖
  28 herramientas en Production, 401 sin token, PKCE S256, código de un solo uso,
  refresh con rotación, credenciales cifradas con AES-256-GCM, estado de sesión por
  identidad de credencial que sobrevive al refresco del token.

- [ ] **5.3 Pedido a infraestructura** — 🏢 *(arrancar YA: mayor tiempo de espera)*
  - [ ] Regla 1: IPs de Anthropic → `mcp-datamart.sincoerp.com` **443/TCP**, con
        certificado TLS válido y proxy inverso.
  - [ ] Regla 2: IP del host central → SQL de cada datamart, **4263 o 1433**.
  - [ ] El proceso como servicio de Windows.
  - [ ] **Revisión de seguridad de la información**, con la custodia de credenciales
        de clientes declarada explícitamente: el radio de daño de una brecha del host
        central pasa a ser *todos los datamarts*.
  *Límite honesto:* cada cliente con datamart propio sigue teniendo que autorizar
  nuestra IP. Es **una** IP en vez de una por sede, pero no es cero gestión.

- [ ] **5.1 Lo que falta de código** — 🤖
  - [ ] `Descripcion` del backend derivada **por sesión** (hoy sale en `estado` y con
        multi-credencial puede filtrar el servidor de otro cliente).
  - [ ] **Almacén de tokens**: hoy en memoria, reiniciar el host desloguea a todos.
        Decidir Redis o SQL — **no** archivo local (el host no debe tener estado en
        disco, para poder ir a Azure).
  - [ ] Revisar `TrustServerCertificate = true` (`Core/Models/DatamartModels.cs:43`):
        el host central hablará con datamarts publicados por internet.
  - [ ] Kestrel fuera de `127.0.0.1`, **solo** detrás del proxy y con 5.3 listo.

- [ ] **5.2 Verificar contra la documentación de desarrollador** — 🤖
  - [ ] ¿Claude exige `/register` (registro dinámico)? Ya está implementado; si no
        hace falta, se quita.
  - [ ] Rangos de IP de Anthropic para la allowlist — **segunda** capa, nunca la primera.

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

## Bloque 6 · Deuda de la auditoría, no atendida

Confirmados por refutación adversarial. Ordenados por lo que le muestran al usuario.
Todos en `DatamartAnalyzer.jsx` salvo donde se indique. — 🤖

- [ ] `~295` El eje Y convierte `NULL` en 0: una celda sin dato se dibuja como una
      barra de cero, en desacuerdo con la tabla de al lado. **Cifra engañosa.**
- [ ] `~1663` Si falla la carga del detalle de un dashboard, se muestra **vacío** en
      vez de reportar el fallo. "No hay nada" es falso.
- [ ] `~1956` `agregarPanel` descarta el resultado y `fetchApi` se traga el error: el
      panel no se crea y no se avisa.
- [ ] `~1770` El `catch` de `handleSelectDb` espera la forma de error de axios que
      `fetchApi` nunca lanza: el aviso accionable de 404 es código muerto.
- [ ] `~1778` Al cambiar de datamart, los paneles del anterior se reejecutan contra la
      base nueva durante la carga. Parcialmente cubierto por la `key`; confirmar en 0.2b.
- [ ] `~1934` Una consulta guardada persiste el SQL con filtros y al reejecutarla se
      rotula con la pregunta sin mencionarlos. Se resuelve de fondo en **2.3**;
      mientras, un chip con los filtros embebidos.
- [ ] `~739` `resp.tipoRespuesta` sin guarda sobre un valor que puede ser `undefined`.
- [ ] `~24` Selector de tema **inerte**: ningún CSS define `theme-day`/`theme-night`.
- [ ] `~1946` Un comentario afirma una validación server-side del gráfico que en el
      árbol del ERP no existe.

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
