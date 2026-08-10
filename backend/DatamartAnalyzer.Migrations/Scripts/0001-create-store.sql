/* ============================================================================
   0001 — Almacén de consultas y dashboards DENTRO del datamart

   Se instala en cada base de datos de datamart, bajo el esquema $schema$
   (ADP_DTM_APP), junto a ADP_DTM_FACT / ADP_DTM_DIM / ADP_DTM_VCONF.

   No hay columna [Database]: la base ES el contexto. Cada datamart lleva
   consigo sus consultas y dashboards, y un backup los incluye.

   El esquema lo crea el runner antes de aplicar migraciones.
   ============================================================================ */

/* ── Consultas guardadas ─────────────────────────────────────────────────── */
CREATE TABLE [$schema$].SavedQueries
(
    Id            uniqueidentifier NOT NULL
                  CONSTRAINT DF_SavedQueries_Id DEFAULT NEWSEQUENTIALID(),
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
GO

CREATE INDEX IX_SavedQueries_CreatedAt
    ON [$schema$].SavedQueries (CreatedAt DESC);
GO

/* ── Dashboards ──────────────────────────────────────────────────────────── */
CREATE TABLE [$schema$].Dashboards
(
    Id          uniqueidentifier NOT NULL
                CONSTRAINT DF_Dashboards_Id DEFAULT NEWSEQUENTIALID(),
    Nombre      nvarchar(200)    NOT NULL,
    Descripcion nvarchar(1000)   NULL,
    CreatedAt   datetime2(0)     NOT NULL
                CONSTRAINT DF_Dashboards_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt   datetime2(0)     NOT NULL
                CONSTRAINT DF_Dashboards_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   nvarchar(128)    NOT NULL
                CONSTRAINT DF_Dashboards_CreatedBy DEFAULT SUSER_SNAME(),

    CONSTRAINT PK_Dashboards PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Dashboards_Nombre UNIQUE (Nombre)
);
GO

/* ── Paneles ─────────────────────────────────────────────────────────────── */
CREATE TABLE [$schema$].DashboardPanels
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
        REFERENCES [$schema$].Dashboards (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Panels_Tipo CHECK (TipoRespuesta IN
        ('Tabla','Grafico','Texto','TablaMasGrafico','TablaMasTexto'))
);
GO

CREATE INDEX IX_Panels_Dashboard_Orden
    ON [$schema$].DashboardPanels (DashboardId, Orden);
GO

/* ── Presets de filtros ──────────────────────────────────────────────────── */
CREATE TABLE [$schema$].FilterPresets
(
    Id          uniqueidentifier NOT NULL
                CONSTRAINT DF_Presets_Id DEFAULT NEWSEQUENTIALID(),
    Nombre      nvarchar(200)    NOT NULL,
    FiltrosJson nvarchar(max)    NOT NULL,
    CreatedAt   datetime2(0)     NOT NULL
                CONSTRAINT DF_Presets_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   nvarchar(128)    NOT NULL
                CONSTRAINT DF_Presets_CreatedBy DEFAULT SUSER_SNAME(),

    CONSTRAINT PK_FilterPresets PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_FilterPresets_Nombre UNIQUE (Nombre)
);
GO
