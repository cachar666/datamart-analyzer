# Plan — Conector MCP remoto, al lado del local

> Respuesta a `docs/HANDOFF-MCP-REMOTO.md`. Decisiones tomadas con Carlos el
> 03/09/2026; las fases A1 y A2 ya están implementadas y verificadas.
> Rama `feat/mcp-remoto`.

---

## El problema

`Datamart ADPRO` es un servidor MCP con transporte **stdio** que corre en el PC de
cada usuario. Los conectores locales no existen en Claude web ni en móvil: es
arquitectura, no un permiso apagado. Para consultar el datamart desde el celular hay
que publicar el **servidor MCP mismo** sobre HTTP y registrarlo como *custom
connector*, para que la conexión salga desde los servidores de Anthropic.

Confusión que conviene despejar: el repo ya tiene dos arquitecturas seleccionables
detrás de `IBackend` — **SQL directo** (`BackendSql`) y **vía API** (`BackendApi`).
**Ninguna resuelve el celular**: en las dos el conector sigue siendo un proceso stdio
en la máquina del usuario. Falta un transporte, no un backend.

**El conector local no se toca.** El remoto vive al lado y los dos conviven: el
`.mcpb` en el escritorio, el remoto en el celular, **los dos contra el mismo almacén
`ADP_DTM_APP`**. Verificado: desde el host HTTP se ven los dashboards `Compras` y
`Prueba conector` creados desde el `.mcpb`.

---

## Decisiones

| # | Decisión | Consecuencia |
|---|---|---|
| 1 | **El conector local no se modifica.** El remoto lo referencia | Cero regresión, cero reempaquetado. El aislamiento se logra por cableado de DI, no por refactor |
| 2 | **OAuth propio: la pantalla pide los cuatro datos del datamart** (servidor, base, usuario, clave), validados abriendo la conexión; el token queda atado a ese datamart | Sirve a clientes y a SINCO desde el día uno, sin depender de Entra ID. **Elimina el login de servicio** de la fase de infraestructura |
| 3 | **Credenciales cifradas en reposo, llave fuera del servidor** | Entra en la revisión de seguridad como decisión explícita |
| 4 | **Host: servidor central en la red de SINCO**, con camino a Azure | Sin estado en disco local, config por `appsettings`, login SQL y no autenticación integrada |
| 5 | **El estado de sesión se indexa por TOKEN y caduca por inactividad** | Impuesto por el spike (§ siguiente). Conserva las 28 herramientas con sus firmas |
| 6 | **Frontend: los dos árboles, `frontend/` como laboratorio** | B1 va primero: es lo que hace viable el porte |
| 7 | **Multi-tenant no entra como registro de clientes** | Pero cada token trae su datamart: el servidor atiende N datamarts sin tabla de tenants |

---

## El spike que cambió el diseño

Ejecutado el 03/09/2026 contra el host real. La pregunta era si el scope de DI del
SDK dura lo que la sesión MCP. **No dura.**

Con `Stateless = false` y `Mcp-Session-Id` estable en las tres llamadas, el scope se
recrea en cada una:

| Llamada | Scope de DI |
|---|---|
| 1 | `13a51477-c0b6-4411-a90c-c592edf714ab` |
| 2 | `a2635bba-a411-434a-9da7-b7b866d3905e` |

Un `IBackend` registrado Scoped **no aísla el estado de sesión: lo destruye**.
`usar_datamart` escribiría y la llamada siguiente vería el campo vacío.
`McpServer.Services` tampoco sirve: no es el proveedor raíz en modo sesión, pero
lanza `InvalidOperationException` al resolver un Scoped.

Y hay una razón de dirección, no solo de implementación: el default del SDK es
`Stateless = true`, y la revisión **2026-07-28 del protocolo (SEP-2567) elimina
`Mcp-Session-Id`** de Streamable HTTP. El modo con sesión es la rama heredada.

**Solución.** El estado se guarda aparte, **indexado por el token de acceso**, y
**caduca por inactividad** (45 min por defecto). Sobrevive al modo stateless, permite
balancear sin afinidad —lo que hará falta el día que esto se mueva a Azure— y conserva
intactos los nombres y las firmas de las 28 herramientas.

Como `BackendBase` no tiene métodos `virtual` y el conector local no se modifica, se
implementa con un **decorador**: hidrata el `BackendSql` interno al construirse y
escribe en los dos lados cuando cambia el estado.

> La caducidad no es limpieza de memoria. Un filtro de empresa olvidado sigue
> recortando cifras sin ninguna señal visible: es preferible tener que volver a
> fijarlo a leer un número filtrado creyendo que es el total.

---

## Estado de las fases

### ✅ A1 · Spike — cerrado

Ver arriba. La medición está en `DiagnosticoTools.cs`, expuesta como `diag_scope`
**solo en Development**: en cualquier otro entorno el catálogo son 28 herramientas,
idéntico al del conector local. Verificado con `tools/list` en Production.

### ✅ A2 · Host HTTP — implementado y verificado

```
backend/DatamartAnalyzer.Mcp.Http/
  DatamartAnalyzer.Mcp.Http.csproj   Sdk.Web + ModelContextProtocol.AspNetCore 2.1.0
                                     + ProjectReference al conector local, sin modificarlo
  Program.cs                         cableado de DI y transporte
  EstadoDeSesiones.cs                el estado por token, con caducidad
  BackendConEstadoPorToken.cs        el decorador que le devuelve memoria al backend
  DiagnosticoTools.cs                solo Development
  appsettings.json                   puerto 5100, caducidad
```

**Lifetimes** — acá está todo el aislamiento entre sesiones:

| Servicio | En el host HTTP | Por qué |
|---|---|---|
| `IBackend` → `BackendConEstadoPorToken` | Scoped | El estado real vive en el almacén por token |
| `BackendSql` | Scoped | Se hidrata en cada llamada desde el almacén |
| `ConexionDatamartDirecta`, `IConexionDatamart` | Scoped | A3: se construirá desde la credencial del token |
| `ISchemaService` | Scoped | Su caché es un `Dictionary` **no concurrente** indexado solo por nombre de base: con varias sesiones, escrituras sin proteger y dos clientes con bases homónimas compartiendo esquema |
| `IStoreMigrator` | Scoped | Mismo problema de clave por nombre de base |
| `ISqlExecutor`, `IStoreRepository` | Scoped | Dependen de `IConexionDatamart`. Un Singleton que depende de un Scoped es una *captive dependency* y el contenedor la rechaza |
| `IEstadoDeSesiones` | **Singleton** | Es lo único que debe sobrevivir entre llamadas |
| `IPrebuiltQueriesService`, `IDocumentService`, `SqlLimites` | Singleton | Sin estado por sesión ni dependencia de la conexión |

**Pruebas que pasan hoy:**

| Prueba | Resultado |
|---|---|
| `fijar_filtros` y después `estado`, mismo token | `empresa: ["NORTE"]` — recordado |
| Lo mismo con token distinto | `filtrosActivos: {}` — aislado |
| Almacén contra `YOSEMITE/SincoComercial_NuevaDW` | `instalado: true`, 2 dashboards, 5 paneles |
| Dashboards visibles desde el host remoto | `Compras` (3) y `Prueba conector` (2) |
| `tools/list` en Production | 28, sin `diag_scope` |
| `git diff` del conector local, `Core` y `connector/` | vacío |

**Puerto 5100 a propósito:** `iniciar.bat` **mata** lo que escuche en 5000, 7001 y
5173 (`:kill_port`, líneas 50-52), así que correrlo apagaría el host.

### ⬜ A3 · Servidor de autorización — lo siguiente

Un OAuth **angosto**: sin base de usuarios, sin recuperación de clave, sin segundo
factor. La identidad *es* la credencial del datamart.

| Endpoint | Qué hace |
|---|---|
| `/.well-known/oauth-protected-resource` | Lo exige la especificación de MCP |
| `/.well-known/oauth-authorization-server` | Metadata del emisor |
| `/authorize` | La pantalla con los cuatro campos. **Valida abriendo una conexión real** antes de emitir el código |
| `/token` | Código por token de acceso + refresco. PKCE obligatorio (OAuth 2.1) |
| `/register` | Registro dinámico de cliente. **Verificar si Claude lo exige**; si no, se omite |

- **Token opaco, no JWT**: referencia a un registro del servidor. La credencial ya vive
  cifrada de nuestro lado y no tiene por qué viajar.
- **Cifrado:** AES-256-GCM con la llave fuera de la configuración. **No DPAPI de
  máquina**: ata el cifrado al servidor y el día que se mueva a Azure obliga a que
  todos vuelvan a autenticarse.
- El error de una credencial equivocada **no debe distinguir** si falló el usuario, la
  clave o la base.
- Al cerrar A3, `ConexionDatamartDirecta` pasa a construirse desde el token y no desde
  `appsettings`. Ya está registrada Scoped para que ese cambio sea de origen del dato,
  no de forma.

### ⬜ A4 · Exposición

`[Authorize]` efectivo sobre el endpoint MCP — efectivo, no declarado; es la lección
del `UseAuthorization()` vacío que tiene la API. Recién acá se escucha fuera de
localhost, y la allowlist de IP de Anthropic es **segunda** capa, nunca la primera.

La `Descripcion` del backend —que incluye servidor y base y se reporta en `estado`—
tiene que derivarse por sesión, o filtra el servidor de otro cliente.

> **A4 no puede exponer nada con A3 abierto.** Sin token, todas las llamadas caen en
> la clave `sin-token:desarrollo` y comparten estado. Es correcto mientras el host
> escuche solo en `127.0.0.1`.

### ⬜ A5 · Infraestructura — reducida por la decisión 2

**Ya no hace falta:** login de servicio, negociación de `GRANT`, instalación del
almacén con cuenta elevada. Cada usuario trae la credencial que ya tiene y
`StoreMigrator` sigue funcionando como hoy.

| # | Origen | Destino | Puerto | Para qué |
|---|---|---|---|---|
| 1 | Rangos de IP de Anthropic | el host central, publicado por HTTPS | 443/TCP | Que el conector llegue |
| 2 | IP del host central | SQL Server de cada datamart | 4263 o 1433 | Que el servidor lea |

Más: certificado TLS válido, proxy inverso, el proceso como servicio, y la revisión de
seguridad de la información — que debe cubrir explícitamente la custodia de
credenciales de clientes (decisión 3).

**Límite honesto:** la regla 2 sigue necesitando que **cada cliente con datamart propio
autorice nuestra IP**. Es una IP en vez de una por sede, pero no es cero gestión.

### ⬜ A6 · Registro del conector

El Owner registra la URL y el client id/secret una vez; después cada persona hace el
flujo y escribe sus cuatro datos. En Team/Enterprise solo los Owners agregan
conectores.

---

## Frente B — la aplicación web

Los dos árboles, `frontend/` como laboratorio y porte verificado a `ADPRO.Informes`.
Corre en paralelo al frente A: repos distintos, ningún archivo compartido.

- **B1 (1 día)** — `normalizeResponse` tiene que **tolerar las dos formas del enum**
  (numérica y texto), no elegir una: eso elimina la divergencia más molesta entre los
  árboles y es lo que permite portar un `.jsx` sin retoques. Y reemplazar las sugeridas
  hardcodeadas por `store.listarSugeridas`, cuyo cliente y endpoint ya existen.
- **B2 (2–3 días)** — modularizar. `App.jsx` son 2.771 líneas pero `App` en sí es
  ~1.200: arriba ya hay 15 componentes separados. El verdadero nudo no es el tamaño,
  son ~40 `useState` y una docena de `useRef` que existen solo para que los callbacks
  vean el valor fresco. Mover archivos no lo arregla; extraer a un reducer sí, y eso
  se decide aparte.

**B1 bloquea B2:** sin la tolerancia a las dos formas del enum, cada porte arrastra un
retoque manual.

---

## Riesgos anotados

- **Acoplamiento por referencia.** El host HTTP referencia `DatamartAnalyzer.Mcp` sin
  modificarlo, así que un cambio futuro en el conector local afecta al remoto en
  silencio. Es el precio de no duplicar el catálogo de herramientas, y es el correcto —
  pero cuando toque modificar el local, hay que compilar y probar los dos.
- **Custodia de credenciales de clientes.** El radio de daño de una brecha del host
  central pasa a ser *todos los datamarts*. Va escrito en la revisión de seguridad.
- **`TrustServerCertificate = true`** en `DatamartConexion.ConstruirConnectionString`
  (`Core/Models/DatamartModels.cs:43`). Con SqlClient 5.x el tráfico va cifrado pero el
  certificado no se valida. Aceptable en LAN; revisarlo antes de que el host central
  hable con datamarts publicados por internet.
- **`CreatedBy` registra la credencial, no la persona.** Con la decisión 2 el
  `SUSER_SNAME()` de `StoreRepository` funciona, porque cada sesión usa la credencial
  de su usuario. Pero si varias personas comparten credencial, son la misma para el
  almacén. Sirve para leer un dashboard, no para auditar.
- **`Core` existe dos veces** — acá y en `Features/DatamartAnalyzer` de
  `ADPRO.Core.API`, y los dos escriben las mismas tablas `ADP_DTM_APP`. Este plan **no
  toca `Core`**, y es una razón más para preferir el cableado sobre el refactor.
- **Registro dinámico de cliente OAuth**: [Especulación] si Claude lo exige. Se
  verifica en A3; puede sumar un endpoint.
- **`manifest.json` declara 21 herramientas y el código expone 28** (faltan las 7 de
  sugeridas y categorías). Corregirlo exige reempaquetar el `.mcpb`: fuera de alcance
  por la decisión 1, anotado para el próximo empaquetado.
