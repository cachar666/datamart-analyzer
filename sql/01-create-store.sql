/* ============================================================================
   Datamart Analyzer — Almacén compartido de consultas y dashboards
   Servidor : YOSEMITE (Windows Authentication)
   Base     : DatamartAnalyzer
   Uso      : sqlcmd -S YOSEMITE -E -i 01-create-store.sql
   Idempotente: se puede ejecutar varias veces sin efectos secundarios.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ── 1. Base de datos ────────────────────────────────────────────────────── */
IF DB_ID(N'DatamartAnalyzer') IS NULL
BEGIN
    PRINT 'Creando base de datos DatamartAnalyzer...';
    CREATE DATABASE [DatamartAnalyzer];
END
ELSE
    PRINT 'La base DatamartAnalyzer ya existe — se conserva.';
GO

USE [DatamartAnalyzer];
GO

/* ── 2. Consultas guardadas ──────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.SavedQueries', N'U') IS NULL
BEGIN
    PRINT 'Creando dbo.SavedQueries...';
    CREATE TABLE dbo.SavedQueries
    (
        Id            uniqueidentifier NOT NULL
                      CONSTRAINT DF_SavedQueries_Id DEFAULT NEWSEQUENTIALID(),
        [Database]    nvarchar(128)    NOT NULL,
        Pregunta      nvarchar(1000)   NOT NULL,
        Sql           nvarchar(max)    NOT NULL,
        TipoRespuesta varchar(20)      NOT NULL
                      CONSTRAINT DF_SavedQueries_Tipo DEFAULT 'Tabla',
        GraficoJson   nvarchar(max)    NULL,
        Tags          nvarchar(400)    NULL,
        CreatedAt     datetime2(0)     NOT NULL
                      CONSTRAINT DF_SavedQueries_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt     datetime2(0)     NOT NULL
                      CONSTRAINT DF_SavedQueries_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy     nvarchar(128)    NOT NULL
                      CONSTRAINT DF_SavedQueries_CreatedBy DEFAULT SUSER_SNAME(),

        CONSTRAINT PK_SavedQueries PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_SavedQueries_Tipo CHECK (TipoRespuesta IN
            ('Tabla','Grafico','Texto','TablaMasGrafico','TablaMasTexto'))
    );

    CREATE INDEX IX_SavedQueries_Database_CreatedAt
        ON dbo.SavedQueries ([Database], CreatedAt DESC);
END
ELSE
    PRINT 'dbo.SavedQueries ya existe.';
GO

/* ── 3. Dashboards ───────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.Dashboards', N'U') IS NULL
BEGIN
    PRINT 'Creando dbo.Dashboards...';
    CREATE TABLE dbo.Dashboards
    (
        Id          uniqueidentifier NOT NULL
                    CONSTRAINT DF_Dashboards_Id DEFAULT NEWSEQUENTIALID(),
        Nombre      nvarchar(200)    NOT NULL,
        [Database]  nvarchar(128)    NOT NULL,
        Descripcion nvarchar(1000)   NULL,
        CreatedAt   datetime2(0)     NOT NULL
                    CONSTRAINT DF_Dashboards_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt   datetime2(0)     NOT NULL
                    CONSTRAINT DF_Dashboards_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy   nvarchar(128)    NOT NULL
                    CONSTRAINT DF_Dashboards_CreatedBy DEFAULT SUSER_SNAME(),

        CONSTRAINT PK_Dashboards PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Dashboards_Nombre_Database UNIQUE ([Database], Nombre)
    );
END
ELSE
    PRINT 'dbo.Dashboards ya existe.';
GO

/* ── 4. Paneles ──────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.DashboardPanels', N'U') IS NULL
BEGIN
    PRINT 'Creando dbo.DashboardPanels...';
    CREATE TABLE dbo.DashboardPanels
    (
        Id                    uniqueidentifier NOT NULL
                              CONSTRAINT DF_Panels_Id DEFAULT NEWSEQUENTIALID(),
        DashboardId           uniqueidentifier NOT NULL,
        Titulo                nvarchar(200)    NOT NULL,
        Sql                   nvarchar(max)    NOT NULL,
        SqlBase               nvarchar(max)    NOT NULL,
        EsPrebuilt            bit              NOT NULL
                              CONSTRAINT DF_Panels_EsPrebuilt DEFAULT 0,
        PrebuiltKey           nvarchar(400)    NULL,
        FiltrosFijosJson      nvarchar(max)    NULL,
        AceptaFiltrosGlobales bit              NOT NULL
                              CONSTRAINT DF_Panels_AceptaFiltros DEFAULT 1,
        TipoRespuesta         varchar(20)      NOT NULL
                              CONSTRAINT DF_Panels_Tipo DEFAULT 'Tabla',
        GraficoJson           nvarchar(max)    NULL,
        Orden                 int              NOT NULL
                              CONSTRAINT DF_Panels_Orden DEFAULT 0,
        CreatedAt             datetime2(0)     NOT NULL
                              CONSTRAINT DF_Panels_CreatedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_DashboardPanels PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Panels_Dashboard FOREIGN KEY (DashboardId)
            REFERENCES dbo.Dashboards (Id) ON DELETE CASCADE,
        CONSTRAINT CK_Panels_Tipo CHECK (TipoRespuesta IN
            ('Tabla','Grafico','Texto','TablaMasGrafico','TablaMasTexto'))
    );

    CREATE INDEX IX_Panels_Dashboard_Orden
        ON dbo.DashboardPanels (DashboardId, Orden);
END
ELSE
    PRINT 'dbo.DashboardPanels ya existe.';
GO

/* ── 5. Presets de filtros ───────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.FilterPresets', N'U') IS NULL
BEGIN
    PRINT 'Creando dbo.FilterPresets...';
    CREATE TABLE dbo.FilterPresets
    (
        Id          uniqueidentifier NOT NULL
                    CONSTRAINT DF_Presets_Id DEFAULT NEWSEQUENTIALID(),
        Nombre      nvarchar(200)    NOT NULL,
        [Database]  nvarchar(128)    NOT NULL,
        FiltrosJson nvarchar(max)    NOT NULL,
        CreatedAt   datetime2(0)     NOT NULL
                    CONSTRAINT DF_Presets_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy   nvarchar(128)    NOT NULL
                    CONSTRAINT DF_Presets_CreatedBy DEFAULT SUSER_SNAME(),

        CONSTRAINT PK_FilterPresets PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_FilterPresets_Nombre UNIQUE ([Database], Nombre)
    );
END
ELSE
    PRINT 'dbo.FilterPresets ya existe.';
GO

/* ── 6. Verificación ─────────────────────────────────────────────────────── */
PRINT '';
PRINT '--- Estado del almacén ---';
SELECT
    t.name                                   AS Tabla,
    (SELECT COUNT(*) FROM sys.columns c
      WHERE c.object_id = t.object_id)       AS Columnas,
    SUM(p.rows)                              AS Filas
FROM sys.tables t
JOIN sys.partitions p
  ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name IN ('SavedQueries','Dashboards','DashboardPanels','FilterPresets')
GROUP BY t.name, t.object_id
ORDER BY t.name;
GO

PRINT '';
PRINT 'Listo. Agregá a appsettings.Development.json:';
PRINT '  "ConnectionStrings": {';
PRINT '    "StoreConnection": "Server=YOSEMITE;Database=DatamartAnalyzer;Integrated Security=True;TrustServerCertificate=True;"';
PRINT '  }';
GO
