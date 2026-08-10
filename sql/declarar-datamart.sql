/* ============================================================================
   Declarar el datamart de un ERP en dbo.ADPConfig

   Se ejecuta SOBRE LA BASE DEL ERP (no sobre el datamart, no sobre master).

   Cuatro claves — los mismos cuatro datos que se entregan al conectar un
   datamart. CnfCodigo es varchar(20): las cuatro entran.

     DATAMARTSRV   servidor   ej. datamart.sincoerp.com,4263   (vacío = el del ERP)
     DATAMART      base       ej. SincoEthussTCoindustrialDW   (obligatoria)
     DATAMARTUSR   usuario    ej. UsuSincoEthussTCoindustrialDW (vacío = Windows auth)
     DATAMARTPWD   clave

   Uso:
     sqlcmd -S SERVIDOR_DEL_ERP -E -d NOMBRE_BD_DEL_ERP -i declarar-datamart.sql

   ATENCIÓN: la clave queda en TEXTO PLANO en dbo.ADPConfig (y su valor previo
   en CnfValueAnterior). Quien pueda leer esa tabla puede leer la contraseña del
   datamart. Si el datamart está en la misma red y el dominio lo permite, dejá
   DATAMARTUSR vacío y usá autenticación de Windows.
   ============================================================================ */

SET NOCOUNT ON;

/* ── 1. Qué hay hoy ──────────────────────────────────────────────────────── */
PRINT '--- Estado actual ---';
PRINT 'Servidor del ERP: ' + @@SERVERNAME;
PRINT 'Base del ERP    : ' + DB_NAME();

SELECT CnfCodigo, CnfValor, CnfDescripcion, CnfTipoDatos, CnfFechaMod
FROM dbo.ADPConfig
WHERE CnfCodigo IN ('DATAMART', 'DATAMARTSRV', 'DATAMARTUSR', 'DATAMARTPWD')
ORDER BY CnfCodigo;

PRINT '';
PRINT '--- CnfTipoDatos más usados (para respetar la convención de la tabla) ---';
SELECT TOP 5 CnfTipoDatos, COUNT(*) AS Registros
FROM dbo.ADPConfig GROUP BY CnfTipoDatos ORDER BY COUNT(*) DESC;
GO

/* ── 2. Parámetros — AJUSTAR ─────────────────────────────────────────────── */

DECLARE @Servidor varchar(800) = N'datamart.sincoerp.com,4263';
DECLARE @Base     varchar(800) = N'CAMBIAME';
DECLARE @Usuario  varchar(800) = NULL;   -- NULL = autenticación de Windows
DECLARE @Clave    varchar(800) = NULL;

/* Ejemplos:

   -- Datamart publicado, autenticación SQL
   SET @Servidor = N'datamart.sincoerp.com,4263';
   SET @Base     = N'SincoEthussTCoindustrialDW';
   SET @Usuario  = N'UsuSincoEthussTCoindustrialDW';
   SET @Clave    = N'********';

   -- Datamart en el mismo servidor del ERP, Windows auth
   SET @Servidor = NULL;
   SET @Base     = N'ADP_DTM_CONSTRUCTORA';
*/

/* ── 3. Aplicar ──────────────────────────────────────────────────────────── */

IF @Base = N'CAMBIAME' OR @Base IS NULL
BEGIN
    PRINT '';
    PRINT '>> No se hizo nada: falta reemplazar @Base por el nombre real.';
END
ELSE
BEGIN
    -- CnfSesion y CnfTipoDatos son NOT NULL: se toma la convención dominante de la
    -- tabla en vez de inventar un valor.
    DECLARE @TipoDatos varchar(50) =
        ISNULL((SELECT TOP 1 CnfTipoDatos FROM dbo.ADPConfig
                GROUP BY CnfTipoDatos ORDER BY COUNT(*) DESC), 'varchar');

    DECLARE @k varchar(20), @v varchar(800), @d varchar(200);

    DECLARE claves CURSOR LOCAL FAST_FORWARD FOR
                  SELECT 'DATAMART',    @Base,     'Datamart de este ERP - base de datos'
        UNION ALL SELECT 'DATAMARTSRV', @Servidor, 'Datamart de este ERP - servidor'
        UNION ALL SELECT 'DATAMARTUSR', @Usuario,  'Datamart de este ERP - usuario SQL'
        UNION ALL SELECT 'DATAMARTPWD', @Clave,    'Datamart de este ERP - clave SQL';

    OPEN claves;
    FETCH NEXT FROM claves INTO @k, @v, @d;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @v IS NOT NULL AND LTRIM(RTRIM(@v)) <> ''
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.ADPConfig WHERE CnfCodigo = @k)
            BEGIN
                UPDATE dbo.ADPConfig
                   SET CnfValueAnterior = CnfValor,
                       CnfValor         = @v,
                       CnfDescripcion   = @d,
                       CnfFechaMod      = GETDATE()
                 WHERE CnfCodigo = @k;
                PRINT '>> Actualizado: ' + @k;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ADPConfig
                    (CnfCodigo, CnfValor, CnfDescripcion, CnfSesion, CnfTipoDatos, CnfFechaMod)
                VALUES (@k, @v, @d, 0, @TipoDatos, GETDATE());
                PRINT '>> Creado: ' + @k;
            END
        END
        ELSE IF EXISTS (SELECT 1 FROM dbo.ADPConfig WHERE CnfCodigo = @k)
        BEGIN
            -- Parámetro no declarado: se limpia para que no quede un valor viejo
            -- (ej. un usuario que ya no aplica porque se pasó a Windows auth).
            DELETE FROM dbo.ADPConfig WHERE CnfCodigo = @k;
            PRINT '>> Eliminado (no declarado): ' + @k;
        END

        FETCH NEXT FROM claves INTO @k, @v, @d;
    END

    CLOSE claves;
    DEALLOCATE claves;
END
GO

/* ── 4. Verificar (la clave se muestra enmascarada) ──────────────────────── */
PRINT '';
PRINT '--- Resultado ---';
SELECT CnfCodigo,
       CASE WHEN CnfCodigo = 'DATAMARTPWD' THEN '********' ELSE CnfValor END AS CnfValor,
       CnfDescripcion, CnfFechaMod
FROM dbo.ADPConfig
WHERE CnfCodigo IN ('DATAMART', 'DATAMARTSRV', 'DATAMARTUSR', 'DATAMARTPWD')
ORDER BY CnfCodigo;
GO

/* ── 5. Para deshacer ────────────────────────────────────────────────────────
   DELETE FROM dbo.ADPConfig
   WHERE CnfCodigo IN ('DATAMART','DATAMARTSRV','DATAMARTUSR','DATAMARTPWD');
   ──────────────────────────────────────────────────────────────────────────── */
