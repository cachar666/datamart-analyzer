# Registro de errores — DatamartAnalyzer

Memoria persistente de errores de ESTE repo. Cada error debe ocurrir una sola vez.
Formato y ciclo: skill `qa-guardian`.

---

### ERR-001: enum-persistido-como-numero

- **Fecha:** 2026-08-10
- **Categoría:** runtime
- **Componente:** backend / Store
- **Mensaje:** (sin excepción — pérdida silenciosa de configuración de gráfico)
- **Causa raíz:** `StoreRepository` definía sus propios `JsonSerializerOptions` sin
  `JsonStringEnumConverter`, mientras que `Program.cs` sí lo registra para la API.
  `ConfiguracionGrafico.Tipo` es el enum `TipoGrafico`, así que al persistir en
  `GraficoJson` se escribía como número (`{"Tipo":0}`). El round-trip interno funcionaba,
  pero cualquier JSON escrito por otra vía (con `"Barras"`) fallaba al deserializar y el
  `catch` devolvía `null` — el panel perdía su gráfico sin ningún error visible.
- **Solución:** agregar `Converters = { new JsonStringEnumConverter() }` a los
  `JsonSerializerOptions` de `StoreRepository`.
- **Prevención:** **SIEMPRE incluir `JsonStringEnumConverter` al crear un
  `JsonSerializerOptions` nuevo en este repo** — hay enums (`TipoRespuesta`, `TipoGrafico`)
  en los DTOs que se serializan tanto a la API como a la base.
- **Ocurrencias:** 1 (detectado en pre-build, no llegó a producción)

---

### ERR-002: recorte-convierte-null-en-vacio

- **Fecha:** 2026-08-10
- **Categoría:** runtime
- **Componente:** backend / Store
- **Mensaje:** (sin excepción — datos degradados)
- **Causa raíz:** el helper `Recortar` devolvía `""` para entradas nulas. Aplicado a
  `PrebuiltKey` (columna nullable), convertía `NULL` en cadena vacía durante la
  importación, borrando la distinción entre "el panel no es prebuilt" y "es prebuilt con
  clave vacía".
- **Solución:** helper aparte `RecortarOpcional` que preserva `null`.
- **Prevención:** **SIEMPRE usar `RecortarOpcional` para columnas nullable** y reservar
  `Recortar` para columnas `NOT NULL`.
- **Ocurrencias:** 1 (detectado en pre-build)

---

---

### ERR-003: ps1-sin-bom-rompe-el-parser

- **Fecha:** 2026-08-10
- **Categoría:** lint
- **Componente:** backend / Api (scripts)
- **Mensaje:** `Token '€" * [Math]::Max(...)' inesperado en la expresión o la instrucción`
- **Causa raíz:** `scripts/build.ps1` se guardó en UTF-8 **sin BOM**. Windows PowerShell 5.1
  (a diferencia de PowerShell 7) asume ANSI cuando no encuentra BOM. El carácter de dibujo
  `─` (U+2500, bytes `E2 94 80`) leído como CP1252 se convierte en `â”€`, y esa `”` es una
  comilla tipográfica que el parser interpreta como apertura de cadena. Cascada de
  "falta el paréntesis de cierre". Los acentos solos no rompen: solo se ven mal (`FallÃ³`).
- **Solución:** guardar los `.ps1` con **BOM UTF-8** y usar guiones ASCII en lugar de
  caracteres de dibujo.
- **Prevención:** **SIEMPRE guardar los `.ps1` de este repo con BOM UTF-8 y sin caracteres
  fuera de ASCII salvo acentos** — nada de `─ │ ┌ └ —`.
- **Ocurrencias:** 1

---

### ERR-004: build-falla-por-exe-bloqueado

- **Fecha:** 2026-08-10
- **Categoría:** compilation
- **Componente:** backend / Api
- **Mensaje:** `MSB3027: No se pudo copiar apphost.exe ... El archivo se ha bloqueado por: "DatamartAnalyzer.Api"`
  y `MSB3021: The process cannot access the file ... because it is being used by another process`
- **Causa raíz:** el backend estaba corriendo (lanzado por `iniciar.bat`) y tenía tomado
  `bin\Debug\net9.0\DatamartAnalyzer.Api.exe`. **No es un error de código**: la
  compilación termina bien y falla recién en el paso de copia. Confunde porque el build
  reporta "error" y hace pensar en un problema de C#.
- **Solución:** detener el proceso antes de compilar. `scripts/build.ps1` ahora lo hace
  solo (filtrando por ruta, para no tocar procesos ajenos al repo) y espera 800 ms a que
  Windows libere el archivo.
- **Prevención:** **Ante MSB3027/MSB3021, buscar el proceso que bloquea antes que un error
  de compilación.** Si el único error es MSB302x, el código compiló bien.
- **Ocurrencias:** 1

## Trampas del perfil genérico que NO aplican a este repo

Anotadas para que ningún agente las "corrija" y rompa algo que funciona.

| Regla del perfil `sql-server` | Por qué no aplica |
|---|---|
| `PropertyNamingPolicy = null` para PascalCase | Este frontend consume **camelCase** (`.tipoRespuesta`, `.sqlGenerado`). Cambiarlo rompe toda la app. |
| `dotnet ef migrations add` ante cambios de modelo | No hay EF Core. Las migraciones son scripts SQL numerados en `backend/DatamartAnalyzer.Migrations/Scripts/`, aplicados por DbUp. |
| `.Include()` para evitar N+1 | No hay navegaciones EF. Las consultas son SQL explícito con Dapper. |
| Estructura Carter + CQRS | Estilo `layered`: Controllers + Services + Repository. |

---

## Deuda conocida (no son errores, pero vigilar)

- `Services/StoreRepository.cs` tiene ~675 líneas, muy por encima de la guía de ~200 del
  perfil. Candidato natural a partirse en `SavedQueriesRepository`, `DashboardsRepository`
  y `FilterPresetsRepository` cuando toque volver a tocarlo.
- `Controllers/Controllers.cs` tiene ~31 KB con cinco controladores. Misma observación.
- La API no tiene autenticación. Aceptable mientras escuche solo en localhost; **bloqueante
  si alguna vez se publica**, porque ahora hay endpoints de escritura.
- `DocumentService` es singleton y lee el corpus RAG al arrancar: editar
  `docs/contexto-erp.md` exige reiniciar el backend.
