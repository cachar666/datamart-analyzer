-- =============================================================================
--  0002 — Preguntas sugeridas
--
--  Hasta ahora el catalogo de preguntas vivia hardcodeado en App.jsx y el
--  contador de usos en localStorage del navegador. Eso significaba que:
--    · el conector MCP no las conocia (Claude no podia sugerirlas),
--    · cada persona veia SUS contadores, en SU navegador, y se perdian al
--      limpiar el cache,
--    · un cliente no podia agregar las suyas ni esconder las que no le sirven.
--
--  Ahora viven acá, dentro del propio datamart, igual que consultas y dashboards.
--
--  La columna Origen es el discriminador:
--    'Sistema' — las que entrega SINCO. Se re-siembran en cada version.
--                El usuario NO puede borrarlas; solo desactivarlas (Activa = 0).
--    'Usuario' — las que agrega el cliente. NUNCA se tocan desde acá.
-- =============================================================================

IF OBJECT_ID('[$schema$].SuggestedQuestions', 'U') IS NULL
BEGIN
    CREATE TABLE [$schema$].SuggestedQuestions
    (
        Id            uniqueidentifier NOT NULL
            CONSTRAINT DF_Sugeridas_Id DEFAULT NEWSEQUENTIALID(),

        -- Identidad estable de las preguntas del sistema, independiente del texto:
        -- asi se puede corregir la redaccion de una sin perder sus usos ni su
        -- estado de oculta. NULL en las del usuario.
        ClaveSistema  varchar(40)      NULL,

        Modulo        varchar(10)      NOT NULL
            CONSTRAINT DF_Sugeridas_Modulo DEFAULT 'ADPRO',
        Categoria     nvarchar(50)     NOT NULL,
        Pregunta      nvarchar(1000)   NOT NULL,

        Origen        varchar(10)      NOT NULL
            CONSTRAINT DF_Sugeridas_Origen DEFAULT 'Usuario',

        -- Ocultar en vez de borrar. Se respeta cuando se re-siembra el catalogo.
        Activa        bit              NOT NULL
            CONSTRAINT DF_Sugeridas_Activa DEFAULT 1,

        Orden         int              NOT NULL
            CONSTRAINT DF_Sugeridas_Orden DEFAULT 0,

        CreatedBy     nvarchar(128)    NOT NULL
            CONSTRAINT DF_Sugeridas_CreatedBy DEFAULT SUSER_SNAME(),
        CreatedAt     datetime2(0)     NOT NULL
            CONSTRAINT DF_Sugeridas_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt     datetime2(0)     NOT NULL
            CONSTRAINT DF_Sugeridas_UpdatedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_SuggestedQuestions PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Sugeridas_Origen CHECK (Origen IN ('Sistema', 'Usuario')),
        CONSTRAINT CK_Sugeridas_Modulo CHECK (Modulo IN ('ADPRO', 'SRM')),

        -- Una pregunta del sistema tiene clave; una del usuario no. Si se
        -- invirtiera, el MERGE del seed pisaria preguntas del cliente.
        CONSTRAINT CK_Sugeridas_Clave CHECK (
            (Origen = 'Sistema' AND ClaveSistema IS NOT NULL) OR
            (Origen = 'Usuario' AND ClaveSistema IS NULL))
    );

    -- Unico solo entre las del sistema: el filtro deja fuera los NULL del usuario.
    CREATE UNIQUE INDEX UQ_Sugeridas_ClaveSistema
        ON [$schema$].SuggestedQuestions (ClaveSistema)
        WHERE ClaveSistema IS NOT NULL;

    CREATE INDEX IX_Sugeridas_Modulo_Cat
        ON [$schema$].SuggestedQuestions (Modulo, Categoria, Orden)
        INCLUDE (Pregunta, Origen, Activa);
END
GO

-- Registro de uso. Se guarda cada vez que alguien lanza una pregunta sugerida,
-- con quien y cuando, para poder mostrar el Top global del equipo Y el personal
-- de cada uno. Un contador en la fila solo daria el global.
IF OBJECT_ID('[$schema$].SuggestedQuestionUsage', 'U') IS NULL
BEGIN
    CREATE TABLE [$schema$].SuggestedQuestionUsage
    (
        Id          bigint           IDENTITY(1,1) NOT NULL,
        QuestionId  uniqueidentifier NOT NULL,
        Usuario     nvarchar(128)    NOT NULL
            CONSTRAINT DF_SugeridasUso_Usuario DEFAULT SUSER_SNAME(),
        UsedAt      datetime2(0)     NOT NULL
            CONSTRAINT DF_SugeridasUso_UsedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_SuggestedQuestionUsage PRIMARY KEY CLUSTERED (Id),
        -- Si se borra una pregunta del usuario, se van sus usos con ella.
        CONSTRAINT FK_SugeridasUso_Pregunta FOREIGN KEY (QuestionId)
            REFERENCES [$schema$].SuggestedQuestions (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_SugeridasUso_Pregunta ON [$schema$].SuggestedQuestionUsage (QuestionId);
    CREATE INDEX IX_SugeridasUso_Usuario  ON [$schema$].SuggestedQuestionUsage (Usuario, QuestionId);
END
GO

-- â”€â”€â”€ Siembra del catalogo del sistema â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
--
-- MERGE sobre ClaveSistema, acotado a Origen = 'Sistema'. Politica acordada:
--   Â· las que no existen  -> se insertan
--   Â· las que ya existen  -> se actualiza texto, categoria, modulo y orden
--   Â· Activa y los usos   -> NO se tocan: si el cliente oculto una, sigue oculta
--   Â· Origen = 'Usuario'  -> NUNCA entra al MERGE, no se pisa ni se borra
--
-- No hay clausula DELETE a proposito: si sacamos una pregunta del catalogo, la
-- que ya esta instalada se queda. Borrarla se llevaria por delante su historial
-- de uso, y el cliente veria desaparecer algo que quiza usaba todos los dias.

;WITH Catalogo (ClaveSistema, Modulo, Categoria, Pregunta, Orden) AS (
  SELECT * FROM (VALUES
  ('ADPRO-001', 'ADPRO', N'Proyectos', N'Listar proyectos con su respectivo estado', 1),
  ('ADPRO-002', 'ADPRO', N'Proyectos', N'Listar los macroproyectos del sistema incluyendo la empresa y sus respectivos codigos', 2),
  ('ADPRO-003', 'ADPRO', N'Costos', N'¿Cuál es el costo ejecutado vs presupuestado por proyecto de construcción?', 3),
  ('ADPRO-004', 'ADPRO', N'Costos', N'¿Cuáles son los capítulos o ítems de obra con mayor desviación entre presupuesto y ejecución?', 4),
  ('ADPRO-005', 'ADPRO', N'Costos', N'¿Cuál es el costo acumulado por tipo de recurso (materiales, mano de obra, equipos) por proyecto?', 5),
  ('ADPRO-006', 'ADPRO', N'Costos', N'¿Cómo ha evolucionado el gasto mensual de obra en cada proyecto este año?', 6),
  ('ADPRO-007', 'ADPRO', N'Costos', N'¿Cuál es el costo por metro cuadrado construido en cada proyecto activo?', 7),
  ('ADPRO-008', 'ADPRO', N'Materiales', N'¿Qué materiales e insumos tienen mayor consumo en los proyectos activos?', 8),
  ('ADPRO-009', 'ADPRO', N'Materiales', N'¿Qué proveedores concentran el mayor gasto en materiales de construcción?', 9),
  ('ADPRO-010', 'ADPRO', N'Materiales', N'¿Cuál es el valor del inventario de mis proyectos en ejecución?', 10),
  ('ADPRO-011', 'ADPRO', N'Materiales', N'Participación porcentual de cada tipo de insumo en el costo consumido total', 11),
  ('ADPRO-012', 'ADPRO', N'Contratos', N'¿Cuáles son los contratos de subcontratistas con mayor valor ejecutado vs contratado?', 12),
  ('ADPRO-013', 'ADPRO', N'Contratos', N'¿Cuál es el valor de actas cobradas por subcontratista en cada proyecto?', 13),
  ('ADPRO-014', 'ADPRO', N'Análisis', N'Pareto de los proyectos según su desviación del asegurado respecto al proyectado (Asegurado mayor que el proyectado), adicionalmente organizados de forma descendente según su ejecución fisica', 14),
  ('ADPRO-015', 'ADPRO', N'Análisis', N'Distribución porcentual por tipo de costo: presupuestado, proyectado y consumido', 15),
  ('ADPRO-016', 'ADPRO', N'Análisis', N'Distribución porcentual por tipo de insumo: presupuestado, proyectado y consumido', 16),
  ('ADPRO-017', 'ADPRO', N'Análisis', N'Proyección de demanda y costo unitario de los principales insumos de compras — tendencia OLS con ajuste estacional', 17),
  ('ADPRO-018', 'ADPRO', N'Análisis', N'¿Cuál es el índice de desempeño del costo (CPI) de cada proyecto activo?', 18),
  ('ADPRO-019', 'ADPRO', N'Proyectos', N'¿Cuántos proyectos hay por ciudad y estado?', 19),
  ('ADPRO-020', 'ADPRO', N'Costos', N'¿Cuál es el costo ejecutado vs presupuestado agrupado por macroproyecto?', 20),
  ('ADPRO-021', 'ADPRO', N'Materiales', N'¿Cuál es el valor mensual de materiales ingresados al almacén este año?', 21),
  ('ADPRO-022', 'ADPRO', N'Materiales', N'¿Cuál es el stock estimado de insumos (entradas menos salidas) en proyectos activos?', 22),
  ('ADPRO-023', 'ADPRO', N'Control', N'IRA — ¿Cuánto se ha desviado el proyectado actual respecto al presupuestado original por proyecto?', 23),
  ('ADPRO-024', 'ADPRO', N'Control', N'CDT — ¿Qué capítulos concentran la mayor parte de la desviación total del proyecto (análisis Pareto por capítulo)?', 24),
  ('ADPRO-025', 'ADPRO', N'Control', N'VAC — ¿Cuánto se estima que costará adicional al presupuesto original y cuál es el costo final estimado (EAC) de cada proyecto?', 25),
  ('ADPRO-026', 'ADPRO', N'Aseguramiento', N'%A — ¿Qué porcentaje del presupuesto proyectado ya está asegurado mediante compras o contratos firmados por proyecto?', 26),
  ('ADPRO-027', 'ADPRO', N'Aseguramiento', N'ICC — ¿Cuánto del dinero ya comprometido (asegurado) se ha convertido efectivamente en costo causado (invertido) por proyecto?', 27),
  ('ADPRO-028', 'ADPRO', N'Aseguramiento', N'RCC — ¿Qué proporción del aseguramiento está cubierta por órdenes de compra versus contratos de subcontrato por proyecto?', 28),
  ('ADPRO-029', 'ADPRO', N'Aseguramiento', N'%NA — ¿Cuánto del presupuesto de cada capítulo aún no tiene respaldo contractual ni orden de compra?', 29),
  ('ADPRO-030', 'ADPRO', N'Eficiencia', N'RR/APU — ¿Cuánto valor ganado se obtiene por cada peso consumido en cada capítulo de obra (rendimiento relativo Ejecutado/Consumido)?', 30),
  ('ADPRO-031', 'ADPRO', N'Eficiencia', N'IONC — ¿Cuánto del insumo causado contablemente (invertido) aún no ha sido consumido en obra por proyecto?', 31),
  ('ADPRO-032', 'ADPRO', N'Eficiencia', N'TQP — ¿A qué ritmo se está quemando el presupuesto mensualmente en cada proyecto activo este año (Tasa de Quemado Presupuestal)?', 32),
  ('ADPRO-033', 'ADPRO', N'Eficiencia', N'SPI — ¿Cuánto falta por invertir para completar cada proyecto (saldo por invertir)?', 33),
  ('ADPRO-034', 'ADPRO', N'Eficiencia', N'RCI — ¿Qué porcentaje del costo causado (invertido) ha sido efectivamente consumido en obra y qué parte permanece en almacén?', 34),
  ('ADPRO-035', 'ADPRO', N'Compras', N'DUC — ¿En cuánto se desvía el valor real de los insumos comprados respecto al presupuestado por ítem?', 35),
  ('ADPRO-036', 'ADPRO', N'Compras', N'IRI — ¿Qué ítems presentan simultáneamente mayor desviación de precio y alto peso dentro del presupuesto (índice de riesgo de ítem)?', 36),
  ('ADPRO-037', 'ADPRO', N'Compras', N'III — ¿Cómo ha evolucionado el precio unitario de los insumos más comprados entre compras sucesivas (inflación interna de insumo)?', 37),
  ('ADPRO-038', 'ADPRO', N'Proyección', N'IAFF — ¿El ritmo de causación contable (invertido) está alineado con el avance físico reportado en cada proyecto?', 38),
  ('ADPRO-039', 'ADPRO', N'Proyección', N'EPP — ¿Qué tan confiable es el proyectado como predictor del costo real invertido en cada proyecto?', 39),
  ('ADPRO-040', 'ADPRO', N'Proyección', N'BIP — Comparativo del desempeño de todas las obras activas del portafolio en los siete conceptos de control (benchmark interno)', 40),
  ('ADPRO-041', 'ADPRO', N'Proyección', N'GMC — ¿Cómo han evolucionado mes a mes los siete conceptos de control (presupuestado, proyectado, asegurado, invertido, consumido, ejecutado) en el portafolio activo?', 41),
  ('SRM-001', 'SRM', N'Licitaciones', N'¿Cuáles son las licitaciones con mayor valor adjudicado?', 1),
  ('SRM-002', 'SRM', N'Licitaciones', N'¿Cuántas licitaciones se han creado por mes y cuál es su valor?', 2),
  ('SRM-003', 'SRM', N'Licitaciones', N'¿Cuáles son las licitaciones más recientes con sus proveedores adjudicados?', 3),
  ('SRM-004', 'SRM', N'Licitaciones', N'¿Cuál es el valor licitado vs presupuestado por actividad?', 4),
  ('SRM-005', 'SRM', N'Proveedores', N'¿Qué proveedores han recibido más adjudicaciones y por qué valor?', 5),
  ('SRM-006', 'SRM', N'Proveedores', N'¿Cuáles son los proveedores adjudicados por proyecto en licitaciones?', 6),
  ('SRM-007', 'SRM', N'Proveedores', N'¿Cuál es el resumen de adjudicaciones por estado del proveedor?', 7),
  ('SRM-008', 'SRM', N'Proveedores', N'¿De qué ciudades provienen los proveedores con mayor valor adjudicado?', 8),
  ('SRM-009', 'SRM', N'Adjudicaciones', N'¿Cuál es el valor total adjudicado en licitaciones por proyecto?', 9),
  ('SRM-010', 'SRM', N'Adjudicaciones', N'¿Qué actividades concentran el mayor valor en las licitaciones?', 10),
  ('SRM-011', 'SRM', N'Adjudicaciones', N'¿Qué insumos se incluyen con mayor valor en las licitaciones?', 11)
  ) AS v (ClaveSistema, Modulo, Categoria, Pregunta, Orden)
)
MERGE [$schema$].SuggestedQuestions AS destino
USING Catalogo AS origen
   ON destino.ClaveSistema = origen.ClaveSistema
  AND destino.Origen = 'Sistema'

WHEN MATCHED AND (destino.Pregunta  <> origen.Pregunta
               OR destino.Categoria <> origen.Categoria
               OR destino.Modulo    <> origen.Modulo
               OR destino.Orden     <> origen.Orden)
THEN UPDATE SET
       destino.Pregunta  = origen.Pregunta,
       destino.Categoria = origen.Categoria,
       destino.Modulo    = origen.Modulo,
       destino.Orden     = origen.Orden,
       destino.UpdatedAt = SYSUTCDATETIME()

WHEN NOT MATCHED BY TARGET
THEN INSERT (ClaveSistema, Modulo, Categoria, Pregunta, Origen, Orden)
     VALUES (origen.ClaveSistema, origen.Modulo, origen.Categoria,
             origen.Pregunta, 'Sistema', origen.Orden);
GO