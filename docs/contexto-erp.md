# SINCO ADPRO — Contexto de Negocio para Análisis de Datamart

## ¿Qué es ADPRO?
SINCO ADPRO es el módulo de Administración de Proyectos de Construcción del ERP SincoERP. Controla presupuesto, compras, inventarios, contratos, ejecución física y proyección de costos de obras de construcción.

## Entidades Principales

**Proyecto / Obra**: Unidad central. Cada obra tiene su propio presupuesto, contratos, compras y control.

**Macroproyecto**: Agrupación de múltiples proyectos.

**EDT (Estructura de Desglose del Trabajo)**: Jerarquía del presupuesto:
- Capítulo → Subcapítulo → Ítem (APU) → Insumo

**Ítem / APU (Análisis de Precio Unitario)**: Actividad presupuestal con cantidad, precio unitario y rendimientos. Ejemplo: "Muro en bloque 20x20".

**Insumo**: Recurso consumido en un ítem. Tipos: materiales, equipos, mano de obra, subcontratos, AIU (Administración/Imprevistos/Utilidad), transporte, IVA. Agrupado en categorías (aceros, concretos, maderas, etc.).

**Versión de presupuesto**: Un proyecto puede tener múltiples versiones; la línea base es la original.

## Conceptos de Control Presupuestal

| Concepto | Significado |
|---|---|
| Presupuestado | Valor original aprobado del presupuesto |
| Proyectado | Presupuesto ajustado con preajustes aprobados (estimado final) |
| Asegurado | Valor comprometido en contratos u órdenes de compra firmadas |
| Consumido | Costo real incurrido (entradas de almacén + actas de obra pagadas) |
| Ejecutado | Avance físico registrado en actas de avance (% completado) |
| Invertido | Pagos efectivamente realizados |
| Faltante | Proyectado - Consumido |

## Flujo de Compras e Inventario
Pedido → Orden de Compra → Entrada de Almacén → Salida a Obra

- **Pedido**: Solicitud de material desde la obra.
- **Orden de Compra (OC)**: Compra aprobada al proveedor.
- **Entrada de Almacén**: Recepción física del material. Afecta inventario y control de costos.
- **Salida de Almacén**: Consumo del material en un ítem del presupuesto. Genera el "consumido".
- **Traslado**: Movimiento de material entre obras o bodegas.
- **Anticipo**: Pago anticipado a proveedor o contratista.

## Contratación y Actas de Obra
- **Contrato**: Acuerdo con contratista para ejecutar ítems del presupuesto. Tipos: general, por grupos, a todo costo.
- **Acta de obra (corte)**: Documento de cobro del contratista por avance ejecutado. Genera el "consumido" de contratos.
- **Acta de avance estándar**: Registro interno del avance físico de actividades.
- **Acta de avance cliente**: Para cobro al cliente (empresa actúa como contratista). Integra con facturación.
- **Retención de garantía**: Porcentaje retenido al contratista hasta finalizar la obra.

## Proyección y Ajustes
- **Preajuste**: Solicitud de cambio al presupuesto proyectado (pendiente de aprobación).
- **Ajuste**: Cambio aprobado al presupuesto proyectado. El presupuesto original no se modifica.
- **Causa de ajuste**: Razón documentada del cambio (ej: diseño, imprevistos, mercado).

## Programación de Obra
- Las **tareas de programación** pueden ser distintas a los ítems del presupuesto.
- El usuario vincula tareas programadas a actividades presupuestales.
- Se controla **programado vs. ejecutado** mes a mes.

## Datamart ADPRO
El datamart extrae datos de ADPRO para análisis y reportes. Usa esquemas:
- `ADP_DTM_FACT`: tablas de hechos (costos, consumos, movimientos)
- `ADP_DTM_DIM`: dimensiones (proyectos, actividades, insumos, terceros, periodos)

Las claves surrogate (SkId*) relacionan hechos con dimensiones. Ejemplo: `SkIdActividad`, `SkIdProyecto`, `SkIdInsumo`.

## Guía SQL del Datamart — Patrones Críticos

### ControlClaseOrigen — REGLA FUNDAMENTAL

La tabla `[ADP_DTM_DIM].[ControlClaseOrigen]` clasifica cada movimiento de `[ADP_DTM_FACT].[ControlProyecto]`.
**SIEMPRE filtrar por `[Clase]` para la clasificación principal. `[Origen]` es el subtipo dentro de cada Clase — úsalo solo para discriminar el tipo de documento cuando ya tienes la Clase.**

#### Valores de `[Clase]` (clasificación principal):

| Clase | Descripción |
|-------|-------------|
| `'P'` | Presupuestado — valor original aprobado |
| `'Y'` | Proyectado — presupuesto ajustado por causas de ajuste |
| `'L'` | Actas Cliente — cobros al cliente (empresa como contratista) |
| `'C'` | Consumido — costo real incurrido |
| `'T'` | Asegurado contratos — valor comprometido en contratos |
| `'B'` | Asegurado compras — valor comprometido en órdenes de compra |
| `'J'` | Ejecutado — avance físico registrado |
| `'I'` | Invertido — pagos efectivamente realizados |

#### Mapeo completo `[Clase]` + `[Origen]` → Tipo de documento:

| Clase | Origen | Tipo de documento |
|-------|--------|-------------------|
| `'P'` | (vacío) | PRESUPUESTO |
| `'Y'` | Y | CAUSA AJUSTE PRESUPUESTO |
| `'L'` | — | ACTAS CLIENTE |
| `'C'` | `S` | SALIDA DE ALMACÉN |
| `'C'` | `D` | REINTEGRO POR SALIDA |
| `'C'` | `G` | ACTAS GENERALES |
| `'C'` | `R` | ACTAS POR GRUPO |
| `'C'` | `T` | ACTAS TODO COSTO |
| `'C'` | `M` | DESCUENTOS POR MENOR VALOR ACTAS |
| `'C'` | `C` | CONTABILIDAD - CUENTAS CONTROL |
| `'C'` | `E` | EQUIPOS |
| `'T'` | `G` | CONTRATOS GENERALES |
| `'T'` | `R` | CONTRATOS POR GRUPOS |
| `'T'` | `T` | CONTRATOS A TODO COSTO |
| `'T'` | `N` | NÓMINA CONTRATADO |
| `'T'` | `C` | CONTABILIDAD - CUENTAS CONTROL |
| `'T'` | `D` | DESCUENTOS POR MENOR VALOR ACTAS |
| `'B'` | `C` | ÓRDENES DE COMPRA |
| `'B'` | `TE` | ENTRADAS POR TRASLADO |
| `'B'` | `TS` | SALIDAS POR TRASLADO |
| `'B'` | `EX` | SALIDAS POR TRANSFORMACIÓN |
| `'B'` | `SX` | ENTRADAS POR TRANSFORMACIÓN |
| `'J'` | `E` | ACTAS DE AVANCE |
| `'I'` | `E` | ENTRADAS ALMACÉN |
| `'I'` | `NP` | ENTRADAS ALMACÉN NO ASIGNADAS AL PRESUPUESTO |
| `'I'` | `N` | CONTROL NÓMINA |
| `'I'` | `EQ` | EQUIPOS |
| `'I'` | `G` | ACTAS GENERALES |
| `'I'` | `R` | ACTAS POR GRUPO |
| `'I'` | `T` | ACTAS TODO COSTO |
| `'I'` | `M` | DESCUENTOS POR MENOR VALOR ACTAS |
| `'I'` | `K` | CONTABILIDAD - CUENTAS CONTROL |
| `'I'` | `V` | NOTAS EN VALOR |
| `'I'` | `VN` | NOTAS EN VALOR SIN ASIGNACIÓN PRESUPUESTAL |
| `'I'` | `X` | DEVOLUCIONES A PROVEEDOR |
| `'I'` | `ED` | DEVOLUCIONES A PROVEEDOR (Entradas Dev.) |
| `'I'` | `TE` | ENTRADAS POR TRASLADO |
| `'I'` | `TS` | SALIDAS POR TRASLADO |
| `'I'` | `EX` | ENTRADAS POR TRANSFORMACIÓN |
| `'I'` | `SX` | SALIDAS POR TRANSFORMACIÓN |
| `'I'` | `AD` | DESCUENTOS CONTABLES ACTAS |
| `'I'` | `SO` | SALDO DE ANTICIPOS O.C. |
| `'I'` | `SC` | SALDO DE ANTICIPOS CONTRATOS |
| `'I'` | `AJ` | AJUSTES DE INVENTARIO |

**Patrón correcto para Presupuestado vs Consumido:**
```sql
SUM(CASE WHEN co.[Clase] = 'P' THEN cp.[Valor Total] ELSE 0 END) AS ValorPresupuestado,
SUM(CASE WHEN co.[Clase] = 'C' THEN cp.[Valor Total] ELSE 0 END) AS ValorConsumido
FROM [ADP_DTM_FACT].[ControlProyecto] cp
INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
```

**Patrón para etiquetar tipo de documento:**
```sql
CASE
  WHEN co.[Clase] = 'P' THEN 'PRESUPUESTO'
  WHEN co.[Clase] = 'Y' THEN 'CAUSA AJUSTE PRESUPUESTO'
  WHEN co.[Clase] = 'L' THEN 'ACTAS CLIENTE'
  WHEN co.[Clase] = 'C' AND co.[Origen] = 'S' THEN 'SALIDA DE ALMACÉN'
  WHEN co.[Clase] = 'C' AND co.[Origen] = 'G' THEN 'ACTAS GENERALES'
  WHEN co.[Clase] = 'C' AND co.[Origen] = 'R' THEN 'ACTAS POR GRUPO'
  WHEN co.[Clase] = 'C' AND co.[Origen] = 'T' THEN 'ACTAS TODO COSTO'
  WHEN co.[Clase] = 'T' AND co.[Origen] = 'G' THEN 'CONTRATOS GENERALES'
  WHEN co.[Clase] = 'B' AND co.[Origen] = 'C' THEN 'ÓRDENES DE COMPRA'
  WHEN co.[Clase] = 'I' AND co.[Origen] = 'E' THEN 'ENTRADAS ALMACÉN'
  ELSE co.[Clase] + '/' + ISNULL(co.[Origen], '')
END AS TipoDocumento
```

**PROHIBIDO**: usar `co.[Origen] = 'P'` — ese valor no existe en `[Origen]`. La `'P'` solo existe en `[Clase]`.

#### Inventario en obra

El **inventario disponible** no es una Clase directa — solo se consideran los movimientos físicos de material, no todos los orígenes de Invertido ni de Consumido:

```
Inventario = Invertido (movimientos físicos) − Consumido (salidas físicas)
```

**Orígenes válidos para Inventario:**
- `[Clase]='I'` con `[Origen] IN ('E','NP','ED','V','VN','X','TE','TS','EX','SX','AJ')`
- `[Clase]='C'` con `[Origen] IN ('S','D')`

Patrón SQL:
```sql
SUM(CASE WHEN co.[Clase] = 'I' AND co.[Origen] IN ('E','NP','ED','V','VN','X','TE','TS','EX','SX','AJ')
         THEN cp.[Valor Total] ELSE 0 END)
- SUM(CASE WHEN co.[Clase] = 'C' AND co.[Origen] IN ('S','D')
         THEN cp.[Valor Total] ELSE 0 END) AS Inventario
```

> Un valor positivo indica material recibido aún no consumido. Un valor negativo indica sobreconsumo respecto a lo invertido.

### Columna de valor en ControlProyecto

Usar siempre `[Valor Total]` (no `[Valor Sin IVA]`) para sumas de presupuesto y costo.

### JOIN con SkIdEmpresa

Las tablas de hechos y dimensiones comparten `SkIdEmpresa` como parte de la clave compuesta. Siempre incluirlo en los JOINs:
```sql
ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
```

### Estado de proyectos

La columna `[ADP_DTM_DIM].[Proyecto].[Estado]` tiene únicamente estos cuatro valores válidos:

| Estado | Descripción |
|--------|-------------|
| `'Presupuesto'` | Proyecto en fase de presupuestación, aún no iniciado |
| `'En ejecucion'` | Proyecto activo en curso (sin tilde) |
| `'Inactivo'` | Proyecto pausado o suspendido |
| `'Finalizado'` | Proyecto terminado y cerrado |

**REGLA**: Cuando el usuario mencione proyectos "activos" o "en curso", filtrar siempre con `WHERE p.[Estado] = 'En ejecucion'`.
**PROHIBIDO**: usar `'Activo'`, `'Terminado'`, `'En planeacion'` — esos valores no existen.

### Columnas con espacios

Muchos campos tienen espacios en el nombre. Usar siempre corchetes:
- `[Nombre Proyecto]`, `[Capitulo Descripcion]`, `[Insumo Descripcion]`, `[Tipo Descripcion]`, `[Salida Valor Total]`, `[Total Entrada]`, `[Valor Total Acta]`

---

# SINCO SRM — Contexto de Negocio

## ¿Qué es SRM?
SINCO SRM es el módulo de Gestión de Relacionamiento con Proveedores del ERP SincoERP. Gestiona el ciclo completo de abastecimiento: vinculación de proveedores, procesos licitatorios, cuadros comparativos, negociación y adjudicación. Se integra nativamente con ADPRO (genera contratos y órdenes de compra) y con A&F (registro de terceros, pagos, retenciones).

Los proveedores se autogestionan a través del portal **ADPROVEEDOR**, cargando su documentación y participando en licitaciones desde allí.

## Submódulos de SRM

**Proveedores**: Administra el ciclo de vinculación, validación y actualización de terceros.
- Invitación de proveedores (nuevos y existentes) al portal ADPROVEEDOR
- Validación documental por especialidad (documentos obligatorios y opcionales)
- Flujos de aprobación multinivel con Oficial de Cumplimiento
- Integración con herramienta de listas vinculantes (COMPLIANCE)
- Notificaciones y recordatorios automáticos individuales o masivos
- Evaluación de proveedores y criterios de preaprobación

**Licitaciones**: Gestiona procesos de abastecimiento desde creación hasta adjudicación.
- Licitación de actividades presupuestadas, proyectadas, no presupuestadas o pedidos de proyecto
- Tipos: privada, abierta, comparativa
- Cuadro comparativo automático por actividad, capítulo o grupo
- Rondas de negociación con versionamiento de cuadros comparativos
- Adjudicación parcial o total a uno o múltiples proponentes
- Generación automática de órdenes de compra y contratos en ADPRO
- Adendas por cambios de fechas, actividades o proveedores

## Conceptos Clave SRM

| Concepto | Significado |
|----------|-------------|
| Proceso licitatorio | Convocatoria estructurada para obtener cotizaciones de proveedores |
| Cuadro comparativo | Análisis consolidado de ofertas económicas de los proponentes |
| Ronda de negociación | Iteración de mejora de precios con los oferentes seleccionados |
| Adjudicación | Asignación formal del contrato u OC al proveedor ganador |
| Adenda | Modificación formal a un proceso licitatorio en curso |
| Habilitado | Proveedor que aprobó el proceso de verificación documental |
| ADPROVEEDOR | Portal web de autogestión para proveedores |

## Integración SRM → ADPRO
Cuando se adjudica una licitación en SRM, el sistema genera automáticamente en ADPRO:
- **Contratos** (generales, por grupos, a todo costo) con los insumos/actividades adjudicados
- **Órdenes de Compra** con los ítems e insumos del proceso licitatorio
La asignación presupuestal de las actividades adjudicadas queda vinculada al presupuesto/proyección del proyecto en ADPRO.
