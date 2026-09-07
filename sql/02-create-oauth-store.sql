/* ============================================================================
   Datamart Analyzer — Almacén de autorización OAuth del host remoto
   Servidor : YOSEMITE (Windows Authentication)
   Base     : DatamartAnalyzer (la misma que 01-create-store.sql — es estado del
              host, no de ningún datamart de cliente)
   Uso      : sqlcmd -S YOSEMITE -E -i 02-create-oauth-store.sql
   Idempotente: se puede ejecutar varias veces sin efectos secundarios.

   Reemplaza AlmacenDeAutorizacionEnMemoria: reiniciar el host ya no invalida
   los tokens de todo el mundo. Una sola tabla para códigos, access tokens y
   refresh tokens — el Tipo los distingue, y así hay un solo lugar donde limpiar
   lo vencido. El secreto en claro NUNCA se guarda: IndiceHash es su SHA-256,
   igual que el índice por hash de la versión en memoria.
   ============================================================================ */

SET NOCOUNT ON;
GO

USE [DatamartAnalyzer];
GO

IF OBJECT_ID(N'dbo.AlmacenAutorizacion', N'U') IS NULL
BEGIN
    PRINT 'Creando dbo.AlmacenAutorizacion...';
    CREATE TABLE dbo.AlmacenAutorizacion
    (
        IndiceHash          char(64)       NOT NULL,   -- SHA-256 hex del secreto; nunca el secreto
        Tipo                tinyint        NOT NULL,   -- 1=Codigo  2=AccessToken  3=RefreshToken
        CredencialProtegida nvarchar(max)  NOT NULL,
        ClienteId           nvarchar(200)  NOT NULL,
        RedirectUri         nvarchar(500)  NULL,        -- solo Tipo=1
        DesafioPkce         nvarchar(200)  NULL,         -- solo Tipo=1
        MetodoPkce          nvarchar(20)   NULL,         -- solo Tipo=1
        ExpiraUtc           datetime2(0)   NOT NULL,
        CreatedAt           datetime2(0)   NOT NULL
                            CONSTRAINT DF_AlmacenAutorizacion_CreatedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_AlmacenAutorizacion PRIMARY KEY CLUSTERED (IndiceHash),
        CONSTRAINT CK_AlmacenAutorizacion_Tipo CHECK (Tipo IN (1, 2, 3))
    );

    CREATE INDEX IX_AlmacenAutorizacion_Expira
        ON dbo.AlmacenAutorizacion (ExpiraUtc);
END
ELSE
    PRINT 'dbo.AlmacenAutorizacion ya existe.';
GO
