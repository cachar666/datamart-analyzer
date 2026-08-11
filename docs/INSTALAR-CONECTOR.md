# Instalar el conector Datamart ADPRO en Claude

Guía para instalar la extensión que conecta Claude con el datamart de ADPRO.
Toma unos dos minutos y no requiere instalar nada más.

---

## Antes de empezar

Necesitás dos cosas:

1. **La aplicación de escritorio de Claude** (Claude Desktop) para Windows o Mac.
   La extensión **no funciona en el navegador ni en el celular** — corre en tu
   computador.

2. **Los cuatro datos de conexión** que te entregaron:

   | Dato | Ejemplo |
   |---|---|
   | Servidor SQL | `datamart.sincoerp.com,4263` |
   | Base Datos | `SincoComercial_NuevaDW` |
   | Usuario | `UsuSincoComercial_NuevaDW` |
   | Clave | `••••••••` |

3. **El archivo del instalador**, según tu computador:

   | Sistema | Archivo |
   |---|---|
   | Windows | `DatamartAnalyzer-ADPRO-win-1.0.0.mcpb` |
   | Mac (Apple Silicon) | `DatamartAnalyzer-ADPRO-mac-1.0.0.mcpb` |

---

## Instalación

> **El doble clic sobre el archivo puede no hacer nada.** En muchos equipos
> Windows la extensión `.mcpb` no está asociada a ninguna aplicación, así que el
> archivo simplemente no se abre. **Usá el paso 2 de abajo**, que funciona siempre.

### 1. Abrí Claude Desktop

### 2. Andá a la pantalla de extensiones

**Configuración → Extensiones → Configuración avanzada → Instalar extensión…**

(en inglés: *Settings → Extensions → Advanced settings → Install Extension…*)

Se abre un explorador de archivos: elegí el `.mcpb` que descargaste.

> **Atajo:** también podés **arrastrar el archivo `.mcpb` y soltarlo encima de la
> ventana de Claude**. Hace lo mismo.

### 3. Revisá y confirmá

Claude muestra el nombre de la extensión, el logo de ADPRO y las herramientas que
trae. Confirmá la instalación.

### 4. Cargá los cuatro datos

Aparece un formulario con los cuatro campos. Copiá y pegá tal cual te los
entregaron, **sin espacios de más al principio ni al final**.

Dos detalles que causan la mayoría de los errores:

- El **Servidor SQL** lleva **coma** antes del puerto, no dos puntos:
  `datamart.sincoerp.com,4263` ✅ · `datamart.sincoerp.com:4263` ❌
- La **Base Datos** es el nombre exacto, normalmente termina en `DW`.

La clave queda guardada de forma segura por tu sistema operativo. No viaja a
ningún lado ni queda escrita en ningún archivo de texto.

### 5. Activá la extensión y reiniciá Claude

Cerrá Claude Desktop **por completo** (no solo la ventana: también desde el
ícono junto al reloj) y volvé a abrirlo.

---

## Comprobar que quedó bien

Abrí una conversación nueva y escribí:

> ¿A qué datamart estás conectado?

Si todo está bien, Claude responde con el nombre del servidor, la base de datos y
te dice que el almacén está instalado.

Probá algo real:

> Listame las empresas que hay en el datamart

---

## Qué podés pedirle

- **Consultar**: *"¿cuál es el costo ejecutado contra el presupuestado por proyecto?"*
- **Guardar**: *"guardá esa consulta como favorita con la etiqueta gerencia"*
- **Dashboards**: *"creá un dashboard llamado Obra Norte y agregale esa consulta"*
- **Filtrar**: *"de acá en adelante filtrá todo por la empresa CONSTRUCTORA DEL NORTE"*

Lo que guardes queda **dentro del propio datamart**, así que lo ves igual desde
Claude que desde la aplicación web. No se guarda en tu computador.

La extensión es **solo de lectura**: no puede modificar ni borrar información del
datamart, ni siquiera si se lo pedís.

---

## Si algo no funciona

### El doble clic no hace nada

Es lo esperado si `.mcpb` no está asociado a ninguna aplicación. Usá
**Configuración → Extensiones → Configuración avanzada → Instalar extensión…**

### No encuentro "Extensiones" en la configuración

Dos causas posibles:

- **La versión de Claude Desktop es vieja.** Actualizala y volvé a mirar.
- **Tu organización bloqueó las extensiones.** Los administradores de Team o
  Enterprise pueden activar una lista blanca desde *Organization settings →
  Connectors → pestaña Desktop*. Con la lista blanca activa, **los usuarios ya no
  pueden instalar extensiones arrastrando ni haciendo clic**: solo se instalan las
  que el administrador autorizó. Si es el caso, hay que pedirle al administrador
  que agregue esta extensión a la lista.

### "No se encontró el servidor o éste no estaba accesible"

La extensión funciona, pero **tu computador no llega al servidor SQL**. No es un
problema de la instalación. Verificá con la gente de infraestructura que tu equipo
tenga salida hacia ese servidor y ese puerto. Desde una terminal:

```powershell
Test-NetConnection datamart.sincoerp.com -Port 4263
```

Si `TcpTestSucceeded` dice `False`, el camino está cerrado y no hay nada que
configurar en Claude que lo arregle.

### "Login failed for user ..."

Usuario o clave incorrectos. Revisá que no se haya colado un espacio al copiar y
pegar. Se corrigen en **Configuración → Extensiones →** la extensión **→
Configuración**, sin reinstalar nada.

### Instalé y no aparece ninguna herramienta

Cerrá Claude Desktop del todo (incluido el ícono junto al reloj) y volvé a
abrirlo. Las extensiones se cargan al arrancar.

### En Mac dice que Apple no puede verificar el desarrollador

El paquete de Mac todavía no está firmado. Para una prueba interna: **Ajustes del
Sistema → Privacidad y seguridad → Abrir de todos modos**. Ver
`docs/CONECTOR-MAC.md`.

---

## Actualizar a una versión nueva

> **Apagá la extensión ANTES de instalar la versión nueva.**
> **Configuración → Extensiones →** apagá el interruptor, esperá dos segundos, y
> recién ahí instalá.

Si no lo hacés, la instalación falla con un error como este:

```
EPERM: operation not permitted, unlink '...\server\coreclr.dll'
```

No es un problema del paquete. Mientras la extensión está encendida, su programa
está corriendo y Windows no deja reemplazar los archivos que tiene abiertos.
Apagar la extensión detiene ese programa y libera los archivos.

Si el error ya apareció, apagá la extensión y volvé a intentar. Si insiste, abrí el
Administrador de tareas, buscá **DatamartAnalyzer.Mcp.exe**, terminalo, y probá de
nuevo.

Con la extensión apagada, instalá el `.mcpb` nuevo por encima del anterior y volvé
a encenderla. Los cuatro datos de conexión se conservan. Las consultas y dashboards
**no se pierden nunca**: viven en el datamart, no en la extensión.

### Cambiar los datos de conexión sin reiniciar Claude

Para que el conector tome datos nuevos no hace falta cerrar la aplicación: apagá el
interruptor de la extensión y volvé a encenderlo. Eso reinicia solo ese conector.

## Desinstalar

**Configuración → Extensiones →** la extensión **→ Desinstalar**. No borra nada del
datamart: tus consultas y dashboards siguen ahí.
