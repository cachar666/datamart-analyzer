# Conector para macOS — firma y notarización

`scripts\empaquetar-conector.ps1 -Plataforma mac` genera
`dist\DatamartAnalyzer-ADPRO-mac-<version>.mcpb` **sin firmar**.

Sirve para probar internamente. **No es apto para entregarle a un cliente todavía**,
y este documento explica por qué y qué falta.

---

## Qué ya está resuelto

| | Estado |
|---|---|
| Binario Apple Silicon (arm64) | Se compila desde Windows. Mach-O 64 verificado. |
| Bit de ejecución dentro del `.mcpb` | `scripts\permisos-mcpb.js` lo aplica (0755). Verificado. |
| Manifest específico de macOS | `platforms: ["darwin"]`, sin `.exe`. Verificado. |
| Firma de Apple | **Falta.** Ver abajo. |
| Prueba en una Mac real | **Falta.** Nunca se ejecutó en macOS. |

El bit de ejecución merece una nota: NTFS no tiene ese concepto, así que al empacar
en Windows todas las entradas del zip quedan con permisos en cero y el binario sale
sin `+x`. Claude Desktop falla con *permission denied* y no queda claro por qué.
`permisos-mcpb.js` reescribe el directorio central del zip para arreglarlo. Es el
tipo de cosa que solo se descubre cuando ya falló en la máquina de un cliente.

---

## Por qué hace falta firmar

macOS le pone el atributo de cuarentena a todo lo que se descarga. Un ejecutable sin
firmar ni notarizar es bloqueado por Gatekeeper con:

> «No se puede abrir "DatamartAnalyzer.Mcp" porque Apple no puede comprobar que no
> contenga malware.»

Para un usuario final sin conocimiento técnico eso es el final del camino.

## Qué se necesita

1. **Apple Developer Program** a nombre de SINCOSOFT S.A.S. — USD 99/año.
2. Un certificado **Developer ID Application** (no el de App Store: este es para
   distribuir fuera de la tienda).
3. Una **Mac** para firmar. `codesign` y `notarytool` solo existen en macOS; desde
   Windows no hay forma.

## Pasos en la Mac

Copiar `dist\DatamartAnalyzer-ADPRO-mac-<version>.mcpb` a la Mac y:

```bash
VERSION=1.0.0
IDENTIDAD="Developer ID Application: SINCOSOFT S.A.S. (TEAMID)"

# 1. Desempacar
mkdir -p mac-build && cd mac-build
unzip -o ../DatamartAnalyzer-ADPRO-mac-$VERSION.mcpb

# 2. Firmar TODO lo nativo: las .dylib primero, el ejecutable al final.
#    El runtime endurecido (--options runtime) es requisito para notarizar.
find server -name '*.dylib' -exec \
  codesign --force --timestamp --options runtime --sign "$IDENTIDAD" {} \;

codesign --force --timestamp --options runtime \
  --entitlements ../entitlements.plist \
  --sign "$IDENTIDAD" server/DatamartAnalyzer.Mcp

codesign --verify --deep --strict --verbose=2 server/DatamartAnalyzer.Mcp

# 3. Notarizar. Apple exige un zip o dmg, no acepta el .mcpb directo.
ditto -c -k --keepParent server ../server-para-notarizar.zip
xcrun notarytool submit ../server-para-notarizar.zip \
  --apple-id "cuenta@sinco.co" --team-id "TEAMID" \
  --password "CLAVE-ESPECIFICA-DE-APP" --wait

# 4. Grapar el ticket al binario, para que valide sin internet.
xcrun stapler staple server/DatamartAnalyzer.Mcp

# 5. Volver a empacar conservando permisos (zip de macOS sí los guarda).
zip -r -X ../DatamartAnalyzer-ADPRO-mac-$VERSION-firmado.mcpb . 
```

`entitlements.plist` mínimo — .NET necesita JIT, y sin esto el runtime endurecido
lo mata al arrancar:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
```

## Verificar que quedó bien

```bash
spctl --assess --type exec -vv server/DatamartAnalyzer.Mcp
# esperado: "accepted" y "source=Notarized Developer ID"
```

---

## Mientras tanto: probarlo sin firmar

Para una prueba interna, en la Mac donde se instale:

```bash
xattr -dr com.apple.quarantine ~/Library/Application\ Support/Claude/Claude\ Extensions/
```

O al primer intento fallido: **Ajustes del Sistema → Privacidad y seguridad →
"Abrir de todos modos"**.

Esto es aceptable para el equipo de SINCO. No se lo pidas a un cliente.

---

## Macs Intel

Hoy solo se genera `osx-arm64` (Apple Silicon, cualquier Mac desde fines de 2020).
Para cubrir Intel hay que agregar `osx-x64` en `empaquetar-conector.ps1` — el
proyecto compila para ese destino sin cambios, es un paquete más.

---

## Lo que no se probó

El binario de macOS **nunca corrió en una Mac**. Se compiló y se verificó su
estructura desde Windows, nada más. La primera vez que se ejecute puede aparecer
cualquier cosa: rutas, permisos, ICU, el propio Gatekeeper. Antes de darlo por bueno
hay que abrirlo en una Mac y confirmar que la herramienta `estado` responde.
