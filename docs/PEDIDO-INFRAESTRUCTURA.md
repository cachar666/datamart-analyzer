# Pedido a Infraestructura — acceso TCP al datamart

*Para: equipo de Infraestructura / Redes*
*De: Carlos Andrés — Desarrollo ADPRO*
*Fecha: 10 de agosto de 2026*

---

## Qué necesito

Que se autorice el acceso TCP saliente al puerto **4263** de
`datamart.sincoerp.com` (**177.253.66.218**) desde la IP pública
**190.61.45.142**.

## Por qué

Estamos desplegando un conector que permite consultar los datamarts de ADPRO en
lenguaje natural desde Claude. El conector se conecta directamente por SQL desde
el equipo del usuario, y hoy ese acceso está bloqueado.

## Evidencia

Estas pruebas se corrieron el 10/08/2026 desde el equipo afectado.

| Prueba | Resultado |
|---|---|
| DNS de `datamart.sincoerp.com` | Resuelve a `177.253.66.218` |
| Ping ICMP al host | **Responde**, 25–42 ms |
| TCP al puerto **443** del mismo host | **ABIERTO** |
| TCP al puerto **4263** | **0 de 8 intentos**, todos `TimedOut` |
| Código de error del socket | `TimedOut` tras 21 segundos |
| IP pública de salida del equipo | `190.61.45.142` |
| Salto de salida en el traceroute | `190.61.45.141` |

## La lectura de esa evidencia

Tres cosas quedan descartadas:

1. **No es DNS**: resuelve correctamente.
2. **El host está vivo**: responde ICMP, y el puerto 443 del mismo servidor acepta
   conexiones desde este mismo equipo. Los paquetes llegan.
3. **No es que el servicio esté caído**: si SQL Server no estuviera escuchando, el
   sistema operativo del servidor respondería con un RST y el socket daría
   `ConnectionRefused` en milisegundos. Lo que obtenemos es `TimedOut` a los 21
   segundos, que es la firma de un paquete **descartado en silencio** — o sea, un
   firewall.

Queda entonces: **algún firewall descarta el tráfico al 4263**, del lado del
servidor o en el camino.

## La hipótesis más probable

Una **lista blanca de IPs** en el firewall del datamart.

El dato que la sugiere: la IP pública de salida es `190.61.45.142` y el salto de
salida en el traceroute es `190.61.45.141` — direcciones vecinas del mismo bloque.
Si la lista blanca contiene solo una de las dos y la salida rota entre ellas,
explica el comportamiento observado: **el mismo equipo consultó el datamart sin
problemas a las 21:00 del mismo día** (consultas respondiendo en 45–70 ms) y a las
23:30 ya no conectaba.

## Preguntas concretas

1. ¿El firewall de `datamart.sincoerp.com` tiene lista blanca de IPs de origen para
   el puerto 4263? Si la tiene, ¿qué IPs están hoy?
2. ¿Está `190.61.45.142` incluida? ¿Y `190.61.45.141`?
3. ¿Nuestra salida a internet rota entre varias IPs públicas? Si es así,
   necesitaríamos autorizar el rango completo.
4. ¿Sophos Connect debería estar conectada para llegar a este servidor? En el
   equipo está el servicio corriendo y el adaptador TAP arriba con IP
   `10.81.236.42`, pero el tráfico al 4263 sale igual por internet.

## Contexto para dimensionar

Esto **no es solo para un equipo**. El conector corre en la máquina de cada
usuario, así que la conexión sale desde la IP de cada oficina. Si el acceso se
controla por lista blanca, habrá que autorizar la salida de cada sede a medida que
se despliegue.

Si eso no es viable, existe una alternativa de arquitectura: publicar la API del
Datamart Analyzer en un servidor que ya tenga acceso al datamart, y que los
conectores hablen con esa API en vez de con SQL directo. En ese esquema hay **una
sola IP que autorizar**. Requiere trabajo de desarrollo previo (la API todavía no
tiene autenticación) y decidir dónde se despliega.

## Cómo verificar que quedó resuelto

Desde el equipo, en PowerShell:

```powershell
Test-NetConnection datamart.sincoerp.com -Port 4263
```

Tiene que decir `TcpTestSucceeded : True`.
