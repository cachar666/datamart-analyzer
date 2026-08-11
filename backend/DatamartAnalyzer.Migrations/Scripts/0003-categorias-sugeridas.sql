-- =============================================================================
--  0003 — Categorias de preguntas sugeridas
--
--  Hasta ahora la categoria era texto suelto en cada pregunta. Para que el usuario
--  pueda CREAR una categoria propia y BORRARLA, hace falta que exista como entidad:
--  si no, no hay forma de distinguir "Tesoreria, que la invento el cliente" de
--  "Costos, que viene con el producto".
--
--  Misma regla que las preguntas:
--    'Sistema' — las 13 que salen del catalogo. No se borran.
--    'Usuario' — las que crea el cliente. Se borran, si no quedan preguntas
--                colgando de ellas.
--
--  La columna Categoria de SuggestedQuestions sigue siendo texto y NO hay clave
--  foranea. Es deliberado: el borrado se rechaza cuando la categoria esta en uso,
--  asi que no puede quedar una pregunta apuntando a una categoria inexistente, y
--  se evita migrar los datos que ya estan sembrados.
-- =============================================================================

IF OBJECT_ID('[$schema$].SuggestedCategories', 'U') IS NULL
BEGIN
    CREATE TABLE [$schema$].SuggestedCategories
    (
        Id        uniqueidentifier NOT NULL
            CONSTRAINT DF_SugCat_Id DEFAULT NEWSEQUENTIALID(),
        Modulo    varchar(10)      NOT NULL
            CONSTRAINT DF_SugCat_Modulo DEFAULT 'ADPRO',
        Nombre    nvarchar(50)     NOT NULL,
        Origen    varchar(10)      NOT NULL
            CONSTRAINT DF_SugCat_Origen DEFAULT 'Usuario',
        Orden     int              NOT NULL
            CONSTRAINT DF_SugCat_Orden DEFAULT 0,
        CreatedBy nvarchar(128)    NOT NULL
            CONSTRAINT DF_SugCat_CreatedBy DEFAULT SUSER_SNAME(),
        CreatedAt datetime2(0)     NOT NULL
            CONSTRAINT DF_SugCat_CreatedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_SuggestedCategories PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_SugCat_Origen CHECK (Origen IN ('Sistema', 'Usuario')),
        CONSTRAINT CK_SugCat_Modulo CHECK (Modulo IN ('ADPRO', 'SRM')),
        -- Dos categorias con el mismo nombre en el mismo modulo serian dos cajones
        -- para lo mismo, y el usuario no podria distinguirlas en pantalla.
        CONSTRAINT UQ_SugCat_Modulo_Nombre UNIQUE (Modulo, Nombre)
    );
END
GO

-- Se siembran a partir de las categorias que YA usan las preguntas del sistema,
-- en vez de repetir la lista a mano. Si el 0002 cambia, esto sigue en sincronia
-- solo. Idempotente: la segunda corrida no inserta nada.
INSERT INTO [$schema$].SuggestedCategories (Modulo, Nombre, Origen, Orden)
SELECT q.Modulo, q.Categoria, 'Sistema', MIN(q.Orden)
FROM [$schema$].SuggestedQuestions q
WHERE q.Origen = 'Sistema'
  AND NOT EXISTS (
      SELECT 1 FROM [$schema$].SuggestedCategories c
      WHERE c.Modulo = q.Modulo AND c.Nombre = q.Categoria)
GROUP BY q.Modulo, q.Categoria;
GO

-- Las categorias que ya invento el usuario antes de que existiera esta tabla
-- (agregando preguntas propias) se registran como suyas, no como del sistema.
INSERT INTO [$schema$].SuggestedCategories (Modulo, Nombre, Origen, Orden)
SELECT q.Modulo, q.Categoria, 'Usuario', 999
FROM [$schema$].SuggestedQuestions q
WHERE q.Origen = 'Usuario'
  AND NOT EXISTS (
      SELECT 1 FROM [$schema$].SuggestedCategories c
      WHERE c.Modulo = q.Modulo AND c.Nombre = q.Categoria)
GROUP BY q.Modulo, q.Categoria;
GO
