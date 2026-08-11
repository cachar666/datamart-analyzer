#!/usr/bin/env node
/*
 * Pone permisos de Unix dentro de un .mcpb empacado en Windows.
 *
 * Por que hace falta: NTFS no tiene bit de ejecucion, asi que cuando `mcpb pack`
 * corre en Windows todas las entradas del zip quedan con atributos externos en
 * cero. Al descomprimir en macOS el binario sale sin +x y Claude Desktop no puede
 * arrancarlo: falla con "permission denied" y no queda claro por que.
 *
 * Esto reescribe, en el directorio central del zip:
 *   - "version made by" -> Unix (3), para que el extractor mire el modo
 *   - atributos externos -> 0755 en ejecutables, 0644 en el resto
 *
 * Sin dependencias a proposito: se ejecuta con el node que ya trajo mcpb.
 *
 * Uso:  node permisos-mcpb.js <archivo.mcpb> [nombre-del-ejecutable ...]
 */

'use strict';
const fs = require('fs');

const FIRMA_EOCD = 0x06054b50;
const FIRMA_CD = 0x02014b50;

const MODO_EJECUTABLE = 0o100755; // regular + rwxr-xr-x
const MODO_NORMAL = 0o100644; // regular + rw-r--r--

function ubicarEocd(buf) {
  // El EOCD esta al final; puede haber hasta 64 KB de comentario despues.
  const desde = Math.max(0, buf.length - 66560);
  for (let i = buf.length - 22; i >= desde; i--) {
    if (buf.readUInt32LE(i) === FIRMA_EOCD) return i;
  }
  throw new Error('No parece un zip: no encontre el End of Central Directory.');
}

function esEjecutable(nombre, extras) {
  if (extras.includes(nombre)) return true;
  const hoja = nombre.split('/').pop();
  // El host de .NET y el volcador de fallos no tienen extension; las nativas si.
  if (hoja === 'createdump') return true;
  if (hoja.endsWith('.dylib') || hoja.endsWith('.so')) return true;
  return false;
}

function main() {
  const [archivo, ...extras] = process.argv.slice(2);
  if (!archivo) {
    console.error('Falta el archivo .mcpb.');
    process.exit(2);
  }

  const buf = fs.readFileSync(archivo);
  const eocd = ubicarEocd(buf);
  const total = buf.readUInt16LE(eocd + 10);
  let pos = buf.readUInt32LE(eocd + 16);

  let ejecutables = 0;
  let normales = 0;

  for (let n = 0; n < total; n++) {
    if (buf.readUInt32LE(pos) !== FIRMA_CD) {
      throw new Error(`Entrada ${n} del directorio central corrupta en el offset ${pos}.`);
    }

    const largoNombre = buf.readUInt16LE(pos + 28);
    const largoExtra = buf.readUInt16LE(pos + 30);
    const largoComentario = buf.readUInt16LE(pos + 32);
    const nombre = buf.toString('utf8', pos + 46, pos + 46 + largoNombre);

    // "version made by": byte alto = sistema de origen. 3 = Unix.
    const versionNecesaria = buf.readUInt8(pos + 4);
    buf.writeUInt16LE((3 << 8) | versionNecesaria, pos + 4);

    const esDirectorio = nombre.endsWith('/');
    let modo;
    if (esDirectorio) modo = 0o040755;
    else if (esEjecutable(nombre, extras)) { modo = MODO_EJECUTABLE; ejecutables++; }
    else { modo = MODO_NORMAL; normales++; }

    // Atributos externos: modo Unix en los 16 bits altos.
    buf.writeUInt32LE((modo << 16) >>> 0, pos + 38);

    pos += 46 + largoNombre + largoExtra + largoComentario;
  }

  fs.writeFileSync(archivo, buf);
  console.log(`  permisos aplicados: ${ejecutables} ejecutable(s) 0755, ${normales} archivo(s) 0644`);
}

main();
