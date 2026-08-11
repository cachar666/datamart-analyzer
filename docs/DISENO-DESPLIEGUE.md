# Diseño del despliegue del conector

Documento para decidir **antes** de escribir código. Plantea el problema, las
opciones reales, una recomendación y las preguntas que hay que responder.

---

## El problema que estamos resolviendo

El conector, en modo SQL directo, abre la conexión **desde la máquina de cada
usuario**. Eso tiene una consecuencia que no es evidente hasta que se despliega:

- La IP de origen es la de la oficina del usuario, no una nuestra.
- Cada sede nueva es un pedido nuevo al firewall.
- Cada usuario necesita credenciales de SQL Server del datamart. Esas credenciales
  quedan en su equipo, y si rotan hay que reconfigurar a todos uno por uno.
- No hay registro de quién consultó qué: para SQL Server todos son el mismo login.

El bloqueo del 10/08 no fue un accidente. Es el primer síntoma de esto.

## Las dos arquitecturas

### A · SQL directo (lo que está funcionando hoy)

```
Claude Desktop ──► conector ──► SQL Server del datamart
   (PC usuario)   (PC usuario)      (red de SINCO)
```

**A favor:** cero infraestructura. Ya funciona. Sirve perfecto para uso interno
del equipo de desarrollo y para clientes que administran su propio datamart.

**En contra:** todo lo del problema de arriba.

### B · Vía API

```
Claude Desktop ──► conector ──► API Datamart Analyzer ──► SQL Server
   (PC usuario)   (PC usuario)     (servidor de SINCO)     (datamart)
```

**A favor:**
- **Una sola IP que autorizar**, la del servidor de la API.
- El usuario **nunca ve las credenciales de SQL**. Rotarlas es un cambio en un
  solo lugar.
- Se puede saber quién consultó qué.
- Un cambio en las reglas de negocio se despliega una vez, no en 200 equipos.

**En contra:** hay que desplegar y mantener un servidor. Y hay un requisito previo
que hoy no se cumple, explicado abajo.

### Recomendación

**Las dos, no una.** Ya están las dos implementadas en el mismo conector y se
eligen por configuración:

| Escenario | Modo |
|---|---|
| Equipo de desarrollo de SINCO, red interna | SQL directo con autenticación de Windows |
| Cliente con datamart propio y pocos usuarios | SQL directo |
| Despliegue a muchos usuarios de un cliente | **Vía API** |
| Usuarios dentro del ERP | **Vía API** (el datamart sale de `dbo.ADPConfig`) |

El modo API es el que falta habilitar, y es el que este documento tiene que
desbloquear.

---

## Bloqueante: la API no tiene autenticación

Verificado en el código el 10/08/2026:

- `Program.cs` llama a `app.UseAuthorization()` pero **no hay ningún esquema de
  autenticación registrado**.
- **No hay un solo `[Authorize]`** en los controladores.
- `POST /api/analyze/execute` acepta **SQL arbitrario** y lo ejecuta.

Hoy no importa porque escucha en `localhost`. **En el momento en que se publique
para que los conectores lleguen a ella, cualquiera que alcance esa URL puede leer
el datamart completo, sin identificarse y sin dejar rastro.**

Publicar la API tal como está no es una opción. La autenticación es requisito, no
mejora.

---

## Diseño propuesto de autenticación

### Qué se descarta y por qué

- **OAuth / Entra ID.** Correcto a futuro, desproporcionado para arrancar. El
  conector corre en stdio y no tiene navegador para el flujo interactivo.
- **Confiar en los headers del ERP** (`X-Previx-Usuario`, `X-Previx-Empresa`).
  Son *afirmaciones del cliente*: cualquiera los puede escribir. Sirven para
  **saber quién** dice ser, no para **verificar** que lo es. Se pueden registrar,
  pero no pueden ser la única barrera.
- **Usuario y clave por persona en la API.** Implica gestionar usuarios, y ya hay
  un directorio: el del ERP. Duplicarlo es crear un segundo lugar donde dar de baja
  a alguien que se fue.

### Lo propuesto: API key por instalación

1. Cada instalación (cliente, o sede) recibe una **clave**.
2. La clave viaja en el header `X-Datamart-Key`.
3. La API guarda **solo el hash** de las claves válidas, nunca la clave.
4. Cada clave **está atada a un datamart**. Una clave de un cliente no puede
   consultar el datamart de otro, aunque adivine el nombre. Esto importa más que
   la autenticación en sí: es lo que impide que un cliente vea datos de otro.
5. Todo request queda registrado: clave usada, datamart, SQL, filas, duración.
6. **HTTPS obligatorio.** Una API key sobre HTTP es una clave en texto plano en la
   red.

En el conector es un campo más en el instalador, junto a la URL de la API. El
usuario final no nota diferencia.

### Lo que esto NO resuelve

Con este diseño, **la API sabe qué instalación consulta, no qué persona**. Si hace
falta trazabilidad por persona —y para un dato financiero probablemente haga
falta— hay que sumar la identidad del ERP encima. Es una decisión a tomar, no algo
que se pueda dejar implícito.

---

## Dónde vive la API

| Opción | A favor | En contra |
|---|---|---|
| **En el servidor del ERP del cliente** | Ya alcanza el datamart; ya está en la red; `dbo.ADPConfig` está ahí mismo | Una instalación por cliente que mantener y actualizar |
| **Un servidor central de SINCO** | Un solo despliegue, una sola IP a autorizar | Tiene que alcanzar el datamart de *cada* cliente; concentra datos de todos |
| **Contenedor junto al datamart** | Aislado, se despliega parejo | Infraestructura nueva por cliente |

**Recomendación: en el servidor del ERP del cliente.** Es donde el modo ERP ya
tiene sentido —el datamart se lee de `dbo.ADPConfig`, la red ya está resuelta— y
evita que SINCO quede en el medio del tráfico de datos de sus clientes, que es un
problema de responsabilidad además de técnico.

---

## Cómo se configura el conector de cada usuario

El `.mcpb` es el mismo. Cambia lo que el usuario carga al instalarlo:

| Campo | SQL directo | Vía API |
|---|---|---|
| Servidor SQL | `datamart.sincoerp.com,4263` | *(vacío)* |
| Base Datos | `SincoComercial_NuevaDW` | *(vacío u opcional)* |
| Usuario / Clave | credenciales SQL | *(vacío)* |
| URL de la API | *(vacío)* | `https://erp.cliente.com/datamart` |
| Clave de la API | *(vacío)* | la que se le entrega |

Falta agregar los dos últimos campos al `manifest.json`. El código del conector ya
elige el modo según lo que reciba.

**A definir:** con seis campos y dos combinaciones válidas, un usuario no técnico
se puede confundir. Vale considerar **dos `.mcpb` distintos** —uno por modo, cada
uno pidiendo solo sus campos— aunque sea el mismo binario adentro.

---

## Actualizaciones

Hoy: se instala el `.mcpb` nuevo encima del anterior y la configuración se
conserva. Funciona, pero es manual y no hay forma de saber quién quedó atrasado.

Para pocos usuarios alcanza. Si esto crece, hay que resolver distribución.

---

## Qué hay que decidir

1. **¿Dónde se despliega la API?** ¿En el ERP de cada cliente, o central?
2. **¿Alcanza con trazabilidad por instalación, o hace falta por persona?**
3. **¿Quién administra las claves de API?** ¿Se generan a mano, hay una pantalla?
4. **¿Un `.mcpb` con los dos modos, o uno por modo?**
5. **¿Quién opera el despliegue en el cliente?** ¿Soporte, o el cliente mismo?

---

## Orden sugerido

1. Autenticación en la API — API key, hash, alcance por datamart, registro. *(es
   el bloqueante)*
2. Campos de URL y clave de API en el `manifest.json` del conector.
3. Prueba completa en un entorno real, con la API en un servidor y el conector en
   otra máquina.
4. Guía de despliegue para soporte.

El paso 1 no depende de ninguna de las decisiones abiertas salvo la 2, y esa se
puede tomar sobre la marcha. Se puede arrancar.
