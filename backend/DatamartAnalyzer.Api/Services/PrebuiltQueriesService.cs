using DatamartAnalyzer.Api.Models;

namespace DatamartAnalyzer.Api.Services;

public interface IPrebuiltQueriesService
{
    PrebuiltQuery? Match(string pregunta);
}

public record PrebuiltQuery(
    string Pregunta,
    string Sql,
    TipoRespuesta TipoRespuesta,
    string ExplicacionTexto,
    ConfiguracionGrafico? Grafico
);

public class PrebuiltQueriesService : IPrebuiltQueriesService
{
    private readonly Dictionary<string, PrebuiltQuery> _queries;

    public PrebuiltQueriesService()
    {
        var list = new List<PrebuiltQuery>
        {
            new(
                Pregunta: "¿Cuál es el costo ejecutado vs presupuestado por proyecto de construcción?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorPresupuestado,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorConsumido,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                        - SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS Desviacion,
                      CASE
                        WHEN SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) = 0 THEN NULL
                        ELSE ROUND(
                          100.0 * SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                                / SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END), 2)
                      END AS PorcentajeEjecucion
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado]
                    HAVING SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) > 0
                    ORDER BY ValorPresupuestado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Comparativa de presupuesto aprobado vs costo real consumido por proyecto, con desviación absoluta y porcentaje de ejecución.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "ValorPresupuestado",
                    CampoAgrupacion: null,
                    Titulo: "Presupuestado vs Consumido por Proyecto",
                    ColorPrimario: "#3b82f6"
                )
            ),

            new(
                Pregunta: "¿Cuáles son los capítulos o ítems de obra con mayor desviación entre presupuesto y ejecución?",
                Sql: """
                    SELECT TOP 20
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      cap.[Capitulo Numero],
                      cap.[Capitulo Descripcion],
                      cap.[Tipo Costo],
                      SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorPresupuestado,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorConsumido,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                        - SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS Desviacion
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[CapituloPresupuesto] cap
                      ON cp.SkIdCapitulo = cap.SkIdCapitulo AND cp.SkIdEmpresa = cap.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      cap.[Capitulo Numero],
                      cap.[Capitulo Descripcion],
                      cap.[Tipo Costo]
                    HAVING SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) > 0
                    ORDER BY ABS(
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                      - SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                    ) DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 capítulos de obra con mayor desviación absoluta entre el presupuesto aprobado y el costo real consumido.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Capitulo Descripcion",
                    CampoEjeY: "Desviacion",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Capítulos con Mayor Desviación Presupuestal",
                    ColorPrimario: "#f59e0b"
                )
            ),

            new(
                Pregunta: "¿Qué materiales e insumos tienen mayor consumo en los proyectos activos?",
                Sql: """
                    SELECT TOP 20
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      i.[Insumo Descripcion],
                      i.[Tipo Descripcion],
                      i.[Agrupacion Descripcion],
                      i.[Unidad],
                      SUM(ISNULL(sa.[Salida Cantidad], 0)) AS CantidadConsumida,
                      SUM(ISNULL(sa.[Salida Valor Total], 0)) AS ValorTotalConsumido
                    FROM [ADP_DTM_FACT].[SalidasAlmacen] sa
                    INNER JOIN [ADP_DTM_DIM].[Insumo] i
                      ON sa.SkIdInsumo = i.SkIdInsumo AND sa.SkIdEmpresa = i.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON sa.SkIdProyecto = p.SkIdProyecto AND sa.SkIdEmpresa = p.SkIdEmpresa
                    WHERE p.[Estado] = 'En ejecucion'
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      i.[Insumo Descripcion],
                      i.[Tipo Descripcion],
                      i.[Agrupacion Descripcion],
                      i.[Unidad]
                    ORDER BY ValorTotalConsumido DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 insumos con mayor valor consumido (salidas de almacén) en proyectos activos, clasificados por tipo y agrupación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Insumo Descripcion",
                    CampoEjeY: "ValorTotalConsumido",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Insumos por Valor Consumido en Proyectos Activos",
                    ColorPrimario: "#10b981"
                )
            ),

            new(
                Pregunta: "¿Cuál es el costo acumulado por tipo de recurso (materiales, mano de obra, equipos) por proyecto?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      cap.[Tipo Costo],
                      SUM(ISNULL(cp.[Valor Total], 0)) AS CostoAcumulado
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[CapituloPresupuesto] cap
                      ON cp.SkIdCapitulo = cap.SkIdCapitulo AND cp.SkIdEmpresa = cap.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE co.[Clase] = 'C'
                      AND cap.[Tipo Costo] IS NOT NULL
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      cap.[Tipo Costo]
                    ORDER BY p.[Nombre Proyecto], CostoAcumulado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Costo real consumido clasificado por tipo de recurso (materiales, mano de obra, equipos, etc.) para cada proyecto.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "CostoAcumulado",
                    CampoAgrupacion: "Tipo Costo",
                    Titulo: "Costo Acumulado por Tipo de Recurso y Proyecto",
                    ColorPrimario: "#8b5cf6"
                )
            ),

            new(
                Pregunta: "¿Cómo ha evolucionado el gasto mensual de obra en cada proyecto este año?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      f.[Año],
                      f.[Mes],
                      f.[NombreMes],
                      f.[MesAño],
                      SUM(ISNULL(cp.[Valor Total], 0)) AS GastoMensual
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Fecha] f
                      ON cp.SkIdFecha = f.SkIdFecha
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE co.[Clase] = 'C'
                      AND f.[Año] = YEAR(GETDATE())
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      f.[Año],
                      f.[Mes],
                      f.[NombreMes],
                      f.[MesAño]
                    ORDER BY p.[Nombre Proyecto], f.[Año], f.[Mes]
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Evolución del gasto mensual real (consumido) por proyecto durante el año en curso.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAño",
                    CampoEjeY: "GastoMensual",
                    CampoAgrupacion: "Nombre Proyecto",
                    Titulo: "Evolución del Gasto Mensual por Proyecto (Año Actual)",
                    ColorPrimario: "#3b82f6"
                )
            ),

            new(
                Pregunta: "¿Cuáles son los contratos de subcontratistas con mayor valor ejecutado vs contratado?",
                Sql: """
                    SELECT TOP 20
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      t.[Nombre] AS NombreSubcontratista,
                      t.[Nit],
                      edc.[No. Contrato],
                      edc.[Descripcion] AS DescripcionContrato,
                      edc.[Clase Contrato],
                      SUM(ISNULL(c.[Valor Total], 0)) AS ValorContratado,
                      SUM(ISNULL(a.[Valor Total Acta], 0)) AS ValorEjecutado,
                      SUM(ISNULL(a.[Valor Total Acta], 0)) - SUM(ISNULL(c.[Valor Total], 0)) AS Diferencia
                    FROM [ADP_DTM_FACT].[Contrato] c
                    INNER JOIN [ADP_DTM_DIM].[EspecificacionDeContratos] edc
                      ON c.SkIdEspecificacionDeContratos = edc.SkIdContrato AND c.SkIdEmpresa = edc.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Tercero] t
                      ON c.SkIdTercero = t.SkIdTercero
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON c.SkIdProyecto = p.SkIdProyecto AND c.SkIdEmpresa = p.SkIdEmpresa
                    LEFT JOIN [ADP_DTM_FACT].[Acta] a
                      ON a.SkIdContrato = c.SkIdEspecificacionDeContratos
                      AND a.SkIdProyecto = c.SkIdProyecto
                      AND a.SkIdEmpresa = c.SkIdEmpresa
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      t.[Nombre],
                      t.[Nit],
                      edc.[No. Contrato],
                      edc.[Descripcion],
                      edc.[Clase Contrato]
                    HAVING SUM(ISNULL(c.[Valor Total], 0)) > 0
                    ORDER BY ValorEjecutado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 contratos de subcontratistas ordenados por valor ejecutado (actas de cobro), comparado contra el valor total contratado.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "NombreSubcontratista",
                    CampoEjeY: "ValorEjecutado",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Subcontratistas: Ejecutado vs Contratado",
                    ColorPrimario: "#f43f5e"
                )
            ),

            new(
                Pregunta: "¿Qué proveedores concentran el mayor gasto en materiales de construcción?",
                Sql: """
                    SELECT TOP 20
                      t.[Nombre] AS NombreProveedor,
                      t.[Nit],
                      t.[Ciudad],
                      t.[Especialidad],
                      COUNT(DISTINCT ea.[SkIdEspecificacionEntradasAlmacen]) AS NumeroEntradas,
                      SUM(ISNULL(ea.[Entrada Valor Sin Iva], 0)) AS ValorSinIVA,
                      SUM(ISNULL(ea.[Entrada Valor Iva], 0)) AS ValorIVA,
                      SUM(ISNULL(ea.[Total Entrada], 0)) AS ValorTotalComprado
                    FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                    INNER JOIN [ADP_DTM_DIM].[Tercero] t
                      ON ea.SkIdTercero = t.SkIdTercero
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON ea.SkIdProyecto = p.SkIdProyecto AND ea.SkIdEmpresa = p.SkIdEmpresa
                    WHERE p.[Estado] = 'En ejecucion'
                    GROUP BY
                      t.[Nombre],
                      t.[Nit],
                      t.[Ciudad],
                      t.[Especialidad]
                    ORDER BY ValorTotalComprado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 proveedores con mayor gasto en materiales (entradas de almacén) en proyectos activos.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Torta,
                    CampoEjeX: "NombreProveedor",
                    CampoEjeY: "ValorTotalComprado",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Proveedores por Gasto en Materiales",
                    ColorPrimario: "#06b6d4"
                )
            ),

            new(
                Pregunta: "¿Cuál es el costo por metro cuadrado construido en cada proyecto activo?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      p.[Ciudad],
                      CAST(NULLIF(p.[AreaConstruidaFinal_M2], 0) AS float) AS AreaConstruidaM2,
                      SUM(ISNULL(cp.[Valor Total], 0)) AS CostoTotalConsumido,
                      CASE
                        WHEN NULLIF(CAST(p.[AreaConstruidaFinal_M2] AS float), 0) IS NULL THEN NULL
                        ELSE ROUND(SUM(ISNULL(cp.[Valor Total], 0)) / CAST(p.[AreaConstruidaFinal_M2] AS float), 2)
                      END AS CostoPorM2
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE p.[Estado] = 'En ejecucion'
                      AND co.[Clase] = 'C'
                      AND p.[AreaConstruidaFinal_M2] IS NOT NULL
                      AND p.[AreaConstruidaFinal_M2] > 0
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      p.[Ciudad],
                      p.[AreaConstruidaFinal_M2]
                    ORDER BY CostoPorM2 DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Costo real por metro cuadrado construido en cada proyecto activo con área registrada.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "CostoPorM2",
                    CampoAgrupacion: null,
                    Titulo: "Costo por M² Construido por Proyecto Activo",
                    ColorPrimario: "#84cc16"
                )
            ),

            new(
                Pregunta: "¿Cuál es el valor del inventario de mis proyectos en ejecución?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Ciudad],
                      SUM(CASE WHEN co.[Clase] = 'I'
                               AND co.[Origen] IN ('E','NP','ED','V','VN','X','TE','TS','EX','SX','AJ')
                               THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                    - SUM(CASE WHEN co.[Clase] = 'C'
                               AND co.[Origen] IN ('S','D')
                               THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS Inventario
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE p.[Estado] = 'En ejecucion'
                    GROUP BY
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Ciudad]
                    HAVING
                      SUM(CASE WHEN co.[Clase] = 'I'
                               AND co.[Origen] IN ('E','NP','ED','V','VN','X','TE','TS','EX','SX','AJ')
                               THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                    - SUM(CASE WHEN co.[Clase] = 'C'
                               AND co.[Origen] IN ('S','D')
                               THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) > 0
                    ORDER BY Inventario DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Valor del inventario disponible (material recibido no consumido) por proyecto en ejecución. Calculado como Invertido (movimientos físicos) menos Consumido (salidas físicas).",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Torta,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "Inventario",
                    CampoAgrupacion: null,
                    Titulo: "Distribución del Inventario por Proyecto en Ejecución",
                    ColorPrimario: "#f59e0b"
                )
            ),

            // ── SRM ──────────────────────────────────────────────────────────────

            new(
                Pregunta: "¿Cuáles son las licitaciones con mayor valor adjudicado?",
                Sql: """
                    SELECT TOP 20
                      [Licitacion Numero],
                      [Licitacion Asunto],
                      [Licitacion Fecha Creacion],
                      [Licitacion Fecha Vigencia],
                      ISNULL([Proyecto Codigo], '') AS [Proyecto Codigo],
                      ISNULL([Proyecto Nombre], 'Sin proyecto') AS [Proyecto Nombre],
                      COUNT(*) AS TotalLineas,
                      MAX(ISNULL([Adjudicacion Valor Global], 0)) AS ValorGlobal,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorDetalleTotal
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY
                      [Licitacion Numero],
                      [Licitacion Asunto],
                      [Licitacion Fecha Creacion],
                      [Licitacion Fecha Vigencia],
                      [Proyecto Codigo],
                      [Proyecto Nombre]
                    ORDER BY ValorGlobal DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 licitaciones ordenadas por valor global adjudicado, con fecha de creación, vigencia y proyecto asociado.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Licitacion Asunto",
                    CampoEjeY: "ValorGlobal",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Licitaciones por Valor Adjudicado",
                    ColorPrimario: "#6366f1"
                )
            ),

            new(
                Pregunta: "¿Qué proveedores han recibido más adjudicaciones y por qué valor?",
                Sql: """
                    SELECT TOP 20
                      [Tercero Nombre],
                      [Tercero Nit],
                      [Tercero Ciudad],
                      [Tercero Estado],
                      COUNT(*) AS TotalLineas,
                      COUNT(CASE WHEN [Licitacion Numero] IS NOT NULL THEN 1 END) AS TotalLicitaciones,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorTotalAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY
                      [Tercero Nombre],
                      [Tercero Nit],
                      [Tercero Ciudad],
                      [Tercero Estado]
                    ORDER BY ValorTotalAdjudicado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 proveedores con mayor valor adjudicado en licitaciones, con ciudad y estado de habilitación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Tercero Nombre",
                    CampoEjeY: "ValorTotalAdjudicado",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Proveedores por Valor Adjudicado",
                    ColorPrimario: "#8b5cf6"
                )
            ),

            new(
                Pregunta: "¿Cuál es el valor total adjudicado en licitaciones por proyecto?",
                Sql: """
                    SELECT
                      ISNULL([Proyecto Codigo], '') AS [Proyecto Codigo],
                      ISNULL([Proyecto Nombre], 'Sin proyecto asignado') AS [Proyecto Nombre],
                      [Proyecto Estado],
                      COUNT(*) AS TotalLineas,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorTotalAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY
                      [Proyecto Codigo],
                      [Proyecto Nombre],
                      [Proyecto Estado]
                    ORDER BY ValorTotalAdjudicado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Valor total adjudicado en licitaciones agrupado por proyecto de obra.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Proyecto Nombre",
                    CampoEjeY: "ValorTotalAdjudicado",
                    CampoAgrupacion: null,
                    Titulo: "Valor Adjudicado en Licitaciones por Proyecto",
                    ColorPrimario: "#3b82f6"
                )
            ),

            new(
                Pregunta: "¿Qué actividades concentran el mayor valor en las licitaciones?",
                Sql: """
                    SELECT TOP 20
                      [Actividad Descripcion],
                      [Actividad Grupo],
                      [Actividad UM],
                      COUNT(*) AS VecesAdjudicada,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorTotalAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    WHERE [Actividad Descripcion] IS NOT NULL
                    GROUP BY
                      [Actividad Descripcion],
                      [Actividad Grupo],
                      [Actividad UM]
                    ORDER BY ValorTotalAdjudicado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 actividades con mayor valor adjudicado en licitaciones, con número de veces adjudicadas.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Actividad Descripcion",
                    CampoEjeY: "ValorTotalAdjudicado",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Actividades por Valor Adjudicado",
                    ColorPrimario: "#f59e0b"
                )
            ),

            new(
                Pregunta: "¿Cuáles son los proveedores adjudicados por proyecto en licitaciones?",
                Sql: """
                    SELECT
                      ISNULL([Proyecto Codigo], '') AS [Proyecto Codigo],
                      ISNULL([Proyecto Nombre], 'Sin proyecto') AS [Proyecto Nombre],
                      [Tercero Nombre],
                      [Tercero Nit],
                      [Tercero Estado],
                      COUNT(*) AS TotalAdjudicaciones,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY
                      [Proyecto Codigo],
                      [Proyecto Nombre],
                      [Tercero Nombre],
                      [Tercero Nit],
                      [Tercero Estado]
                    ORDER BY [Proyecto Nombre], ValorAdjudicado DESC
                    """,
                TipoRespuesta: TipoRespuesta.Tabla,
                ExplicacionTexto: "Listado de proveedores adjudicados en licitaciones organizado por proyecto, con valor total adjudicado a cada uno.",
                Grafico: null
            ),

            new(
                Pregunta: "¿Cuántas licitaciones se han creado por mes y cuál es su valor?",
                Sql: """
                    SELECT
                      SUBSTRING([Licitacion Fecha Creacion], 4, 2) AS Mes,
                      SUBSTRING([Licitacion Fecha Creacion], 7, 4) AS Anio,
                      SUBSTRING([Licitacion Fecha Creacion], 4, 2) + '/' + SUBSTRING([Licitacion Fecha Creacion], 7, 4) AS MesAnio,
                      COUNT(*) AS TotalLineas,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorTotalAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    WHERE [Licitacion Fecha Creacion] IS NOT NULL
                      AND LEN([Licitacion Fecha Creacion]) >= 10
                    GROUP BY
                      SUBSTRING([Licitacion Fecha Creacion], 4, 2),
                      SUBSTRING([Licitacion Fecha Creacion], 7, 4)
                    ORDER BY Anio, Mes
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Evolución mensual del valor adjudicado en licitaciones, agrupado por mes y año de creación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAnio",
                    CampoEjeY: "ValorTotalAdjudicado",
                    CampoAgrupacion: null,
                    Titulo: "Valor Adjudicado por Mes de Creación",
                    ColorPrimario: "#10b981"
                )
            ),

            new(
                Pregunta: "¿Cuál es el valor licitado vs presupuestado por actividad?",
                Sql: """
                    SELECT TOP 20
                      [Licitacion Numero],
                      [Licitacion Asunto],
                      [Actividad Descripcion],
                      [Actividad UM],
                      SUM(ISNULL([Licitacion Cantidad Presupuestada], 0)) AS CantidadPresupuestada,
                      SUM(ISNULL([Licitacion Actividad Cantidad], 0)) AS CantidadLicitada,
                      SUM(ISNULL([Licitacion Valor Unitario Presupuestado], 0)) AS ValorUnitarioPresupuestado,
                      SUM(ISNULL([Licitacion Valor Total], 0)) AS ValorTotalLicitado
                    FROM [SRM_DTM_VFACT].[LicitacionActividad]
                    GROUP BY
                      [Licitacion Numero],
                      [Licitacion Asunto],
                      [Actividad Descripcion],
                      [Actividad UM]
                    HAVING SUM(ISNULL([Licitacion Cantidad Presupuestada], 0)) > 0
                    ORDER BY ValorTotalLicitado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Comparativa entre cantidad y valor presupuestado vs lo licitado por actividad en cada proceso.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Actividad Descripcion",
                    CampoEjeY: "ValorTotalLicitado",
                    CampoAgrupacion: null,
                    Titulo: "Valor Licitado vs Presupuestado por Actividad",
                    ColorPrimario: "#f43f5e"
                )
            ),

            new(
                Pregunta: "¿Qué insumos se incluyen con mayor valor en las licitaciones?",
                Sql: """
                    SELECT TOP 20
                      [Insumo Descripcion],
                      [Insumo Tipo Descripcion],
                      [Insumo Agrupacion Descripcion],
                      [Insumo Unidad],
                      COUNT(*) AS VecesIncluido,
                      SUM(ISNULL([Licitacion Actividad Cantidad], 0)) AS CantidadTotal,
                      SUM(ISNULL([Licitacion Valor Total], 0)) AS ValorTotal
                    FROM [SRM_DTM_VFACT].[LicitacionActividad]
                    WHERE [Insumo Descripcion] IS NOT NULL
                      AND LEN(ISNULL([Insumo Descripcion], '')) > 0
                    GROUP BY
                      [Insumo Descripcion],
                      [Insumo Tipo Descripcion],
                      [Insumo Agrupacion Descripcion],
                      [Insumo Unidad]
                    ORDER BY ValorTotal DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 insumos con mayor valor total incluidos en procesos de licitación, clasificados por tipo y agrupación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Insumo Descripcion",
                    CampoEjeY: "ValorTotal",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Insumos por Valor en Licitaciones",
                    ColorPrimario: "#06b6d4"
                )
            ),

            new(
                Pregunta: "¿Cuál es el resumen de adjudicaciones por estado del proveedor?",
                Sql: """
                    SELECT
                      ISNULL([Tercero Estado], 'Sin estado') AS [Tercero Estado],
                      ISNULL([Tercero Categoria], 'Sin categoría') AS [Tercero Categoria],
                      COUNT(*) AS TotalAdjudicaciones,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorTotal
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY
                      [Tercero Estado],
                      [Tercero Categoria]
                    ORDER BY ValorTotal DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Distribución de adjudicaciones y valor total según el estado de habilitación y categoría del proveedor.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Torta,
                    CampoEjeX: "Tercero Estado",
                    CampoEjeY: "ValorTotal",
                    CampoAgrupacion: null,
                    Titulo: "Adjudicaciones por Estado del Proveedor",
                    ColorPrimario: "#a855f7"
                )
            ),

            new(
                Pregunta: "¿Cuáles son las licitaciones más recientes con sus proveedores adjudicados?",
                Sql: """
                    SELECT TOP 30
                      [Licitacion Numero],
                      [Licitacion Asunto],
                      [Licitacion Fecha Creacion],
                      [Licitacion Fecha Vigencia],
                      ISNULL([Proyecto Codigo], '') AS [Proyecto Codigo],
                      ISNULL([Proyecto Nombre], 'Sin proyecto') AS [Proyecto Nombre],
                      [Tercero Nombre],
                      [Tercero Nit],
                      [Tercero Estado],
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY
                      [Licitacion Numero],
                      [Licitacion Asunto],
                      [Licitacion Fecha Creacion],
                      [Licitacion Fecha Vigencia],
                      [Proyecto Codigo],
                      [Proyecto Nombre],
                      [Tercero Nombre],
                      [Tercero Nit],
                      [Tercero Estado]
                    ORDER BY [Licitacion Fecha Creacion] DESC, ValorAdjudicado DESC
                    """,
                TipoRespuesta: TipoRespuesta.Tabla,
                ExplicacionTexto: "Las 30 adjudicaciones más recientes con detalle de licitación, proveedor adjudicado y valor.",
                Grafico: null
            ),
        };

        _queries = list.ToDictionary(
            q => NormalizeKey(q.Pregunta),
            q => q
        );
    }

    public PrebuiltQuery? Match(string pregunta)
    {
        _queries.TryGetValue(NormalizeKey(pregunta), out var match);
        return match;
    }

    private static string NormalizeKey(string s)
        => s.Trim().ToLowerInvariant()
             .Replace("¿", "").Replace("?", "")
             .Replace("  ", " ");
}
