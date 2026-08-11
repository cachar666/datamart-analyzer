using DatamartAnalyzer.Core.Models;

namespace DatamartAnalyzer.Core.Prebuilt;

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

            new(
                Pregunta: "Pareto de los proyectos según su desviación del asegurado respecto al proyectado (Asegurado mayor que el proyectado), adicionalmente organizados de forma descendente según su ejecución fisica",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto],
                        p.[Nombre Proyecto],
                        p.[Estado],
                        SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorProyectado,
                        SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorAsegurado,
                        SUM(CASE WHEN co.[Clase] = 'J' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorEjecutado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                        ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                        ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado]
                      HAVING SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) > 0
                         AND SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                           > SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                    ),
                    Calc AS (
                      SELECT *,
                        ValorAsegurado - ValorProyectado AS Desviacion,
                        CASE WHEN ValorProyectado = 0 THEN NULL
                             ELSE ROUND(100.0 * (ValorAsegurado - ValorProyectado) / ValorProyectado, 2)
                        END AS PctDesviacion,
                        CASE WHEN ValorProyectado = 0 THEN NULL
                             ELSE ROUND(100.0 * ValorEjecutado / ValorProyectado, 2)
                        END AS PctEjecucionFisica
                      FROM Base
                    )
                    SELECT
                      [Codigo Proyecto],
                      [Nombre Proyecto],
                      [Estado],
                      ValorProyectado,
                      ValorAsegurado,
                      ValorEjecutado,
                      Desviacion,
                      PctDesviacion,
                      PctEjecucionFisica,
                      ROUND(
                        100.0 * SUM(ABS(Desviacion)) OVER (ORDER BY ABS(Desviacion) DESC
                                  ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
                              / NULLIF(SUM(ABS(Desviacion)) OVER (), 0),
                        2
                      ) AS ParetoAcumuladoPct
                    FROM Calc
                    ORDER BY ABS(Desviacion) DESC, PctEjecucionFisica DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Análisis Pareto de proyectos donde el valor asegurado supera al proyectado (desviación positiva). Ordenado por mayor desviación descendente con porcentaje acumulado Pareto y ejecución física.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "Desviacion",
                    CampoAgrupacion: null,
                    Titulo: "Pareto de Proyectos: Desviación Asegurado vs Proyectado",
                    ColorPrimario: "#f43f5e"
                )
            ),

            new(
                Pregunta: "Participación porcentual de cada tipo de insumo en el costo consumido total",
                Sql: """
                    SELECT
                      p.[Estado],
                      i.[Tipo Descripcion],
                      SUM(ISNULL(sa.[Salida Valor Total], 0)) AS CostoConsumido,
                      ROUND(
                        100.0 * SUM(ISNULL(sa.[Salida Valor Total], 0))
                              / NULLIF(SUM(SUM(ISNULL(sa.[Salida Valor Total], 0))) OVER (), 0),
                        2
                      ) AS PesoPorcentual
                    FROM [ADP_DTM_FACT].[SalidasAlmacen] sa
                    INNER JOIN [ADP_DTM_DIM].[Insumo] i
                      ON sa.SkIdInsumo = i.SkIdInsumo AND sa.SkIdEmpresa = i.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON sa.SkIdProyecto = p.SkIdProyecto AND sa.SkIdEmpresa = p.SkIdEmpresa
                    WHERE i.[Tipo Descripcion] IS NOT NULL
                    GROUP BY p.[Estado], i.[Tipo Descripcion]
                    ORDER BY CostoConsumido DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Participación porcentual de cada tipo de insumo en el costo consumido total (salidas de almacén), desglosada por estado del proyecto. El porcentaje es relativo al gran total de todos los proyectos y estados.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Torta,
                    CampoEjeX: "Tipo Descripcion",
                    CampoEjeY: "CostoConsumido",
                    CampoAgrupacion: null,
                    Titulo: "Participación por Tipo de Insumo en el Costo Consumido Total",
                    ColorPrimario: "#06b6d4"
                )
            ),

            new(
                Pregunta: "Distribución porcentual por tipo de costo: presupuestado, proyectado y consumido",
                Sql: """
                    SELECT
                      p.[Estado],
                      cap.[Tipo Costo],
                      SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorPresupuestado,
                      SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorProyectado,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorConsumido,
                      ROUND(
                        100.0 * SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                              / NULLIF(SUM(SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)) OVER (), 0),
                        2) AS PctPresupuestado,
                      ROUND(
                        100.0 * SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                              / NULLIF(SUM(SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)) OVER (), 0),
                        2) AS PctProyectado,
                      ROUND(
                        100.0 * SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                              / NULLIF(SUM(SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)) OVER (), 0),
                        2) AS PctConsumido
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[CapituloPresupuesto] cap
                      ON cp.SkIdCapitulo = cap.SkIdCapitulo AND cp.SkIdEmpresa = cap.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE co.[Clase] IN ('P','Y','C')
                      AND cap.[Tipo Costo] IS NOT NULL
                    GROUP BY p.[Estado], cap.[Tipo Costo]
                    ORDER BY ValorConsumido DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Distribución porcentual del presupuestado, proyectado y consumido por tipo de costo (materiales, mano de obra, equipos, etc.) y estado del proyecto. Los porcentajes son independientes para cada clase: cada % indica qué parte del total de esa clase representa ese tipo de costo.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Tipo Costo",
                    CampoEjeY: "ValorConsumido",
                    CampoAgrupacion: "Estado",
                    Titulo: "Distribución por Tipo de Costo: Presupuestado, Proyectado y Consumido",
                    ColorPrimario: "#8b5cf6"
                )
            ),

            new(
                Pregunta: "Distribución porcentual por tipo de insumo: presupuestado, proyectado y consumido",
                Sql: """
                    SELECT
                      p.[Estado],
                      i.[Tipo Descripcion],
                      SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorPresupuestado,
                      SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorProyectado,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorConsumido,
                      ROUND(
                        100.0 * SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                              / NULLIF(SUM(SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)) OVER (), 0),
                        2) AS PctPresupuestado,
                      ROUND(
                        100.0 * SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                              / NULLIF(SUM(SUM(CASE WHEN co.[Clase] = 'Y' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)) OVER (), 0),
                        2) AS PctProyectado,
                      ROUND(
                        100.0 * SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                              / NULLIF(SUM(SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)) OVER (), 0),
                        2) AS PctConsumido
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Insumo] i
                      ON cp.SkIdInsumo = i.SkIdInsumo AND cp.SkIdEmpresa = i.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE co.[Clase] IN ('P','Y','C')
                      AND i.[Tipo Descripcion] IS NOT NULL
                    GROUP BY p.[Estado], i.[Tipo Descripcion]
                    ORDER BY p.[Estado], i.[Tipo Descripcion]
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Distribución porcentual del presupuestado (P), proyectado (Y) y consumido (C) por tipo de insumo y estado del proyecto. Cada porcentaje es relativo al total de su propia clase.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Tipo Descripcion",
                    CampoEjeY: "ValorConsumido",
                    CampoAgrupacion: "Estado",
                    Titulo: "Distribución por Tipo de Insumo: Presupuestado, Proyectado y Consumido",
                    ColorPrimario: "#8b5cf6"
                )
            ),

            new(
                Pregunta: "Proyección de demanda y costo unitario de los principales insumos de compras — tendencia OLS con ajuste estacional",
                Sql: """
                    WITH
                    TopInsumos AS (
                      SELECT TOP 5
                        ea.SkIdInsumo, ea.SkIdEmpresa,
                        i.[Insumo Descripcion], i.[Tipo Descripcion],
                        SUM(ISNULL(ea.[Total Entrada], 0)) AS TotalComprado
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      INNER JOIN [ADP_DTM_DIM].[Insumo] i
                        ON ea.SkIdInsumo = i.SkIdInsumo AND ea.SkIdEmpresa = i.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[Fecha] f
                        ON ea.SkIdFechaEntrada = f.SkIdFecha
                      WHERE f.[Fecha] >= DATEADD(MONTH, -36, CAST(GETDATE() AS DATE))
                      GROUP BY ea.SkIdInsumo, ea.SkIdEmpresa, i.[Insumo Descripcion], i.[Tipo Descripcion]
                      ORDER BY TotalComprado DESC
                    ),
                    Historico AS (
                      SELECT
                        ti.[Insumo Descripcion], ti.[Tipo Descripcion],
                        f.[Año], f.[Mes], f.[MesAño],
                        DATEDIFF(MONTH,
                          DATEADD(MONTH, -36, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)),
                          DATEFROMPARTS(f.[Año], f.[Mes], 1)) + 1 AS NumMes,
                        SUM(ISNULL(ea.[Entrada Cantidad], 0)) AS Cantidad,
                        CASE WHEN SUM(ISNULL(ea.[Entrada Cantidad], 0)) > 0
                             THEN SUM(ISNULL(ea.[Total Entrada], 0)) / SUM(ISNULL(ea.[Entrada Cantidad], 0))
                             ELSE NULL END AS CostoUnitario
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      INNER JOIN TopInsumos ti ON ea.SkIdInsumo = ti.SkIdInsumo AND ea.SkIdEmpresa = ti.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[Fecha] f ON ea.SkIdFechaEntrada = f.SkIdFecha
                      WHERE f.[Fecha] >= DATEADD(MONTH, -36, CAST(GETDATE() AS DATE))
                        AND f.[Fecha] < DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
                      GROUP BY ti.[Insumo Descripcion], ti.[Tipo Descripcion], f.[Año], f.[Mes], f.[MesAño]
                    ),
                    OLS AS (
                      SELECT
                        [Insumo Descripcion], [Tipo Descripcion],
                        AVG(CAST(NumMes AS FLOAT)) AS MedX,
                        AVG(Cantidad)     AS MedCantidad,
                        AVG(CostoUnitario) AS MedCosto,
                        (SUM(CAST(NumMes AS FLOAT) * Cantidad)
                          - COUNT(*) * AVG(CAST(NumMes AS FLOAT)) * AVG(Cantidad))
                        / NULLIF(SUM(CAST(NumMes AS FLOAT) * CAST(NumMes AS FLOAT))
                          - COUNT(*) * AVG(CAST(NumMes AS FLOAT)) * AVG(CAST(NumMes AS FLOAT)), 0) AS b1Cantidad,
                        (SUM(CAST(NumMes AS FLOAT) * CostoUnitario)
                          - COUNT(*) * AVG(CAST(NumMes AS FLOAT)) * AVG(CostoUnitario))
                        / NULLIF(SUM(CAST(NumMes AS FLOAT) * CAST(NumMes AS FLOAT))
                          - COUNT(*) * AVG(CAST(NumMes AS FLOAT)) * AVG(CAST(NumMes AS FLOAT)), 0) AS b1Costo
                      FROM Historico
                      WHERE CostoUnitario IS NOT NULL
                      GROUP BY [Insumo Descripcion], [Tipo Descripcion]
                    ),
                    IndiceEstacional AS (
                      SELECT
                        h.[Insumo Descripcion], h.[Mes],
                        AVG(h.Cantidad / NULLIF(
                          o.MedCantidad + o.b1Cantidad * (CAST(h.NumMes AS FLOAT) - o.MedX), 0)) AS IECantidad,
                        AVG(h.CostoUnitario / NULLIF(
                          o.MedCosto + o.b1Costo * (CAST(h.NumMes AS FLOAT) - o.MedX), 0)) AS IECosto
                      FROM Historico h
                      INNER JOIN OLS o ON h.[Insumo Descripcion] = o.[Insumo Descripcion]
                      WHERE h.CostoUnitario IS NOT NULL
                      GROUP BY h.[Insumo Descripcion], h.[Mes]
                    ),
                    Forecast AS (
                      SELECT
                        o.[Insumo Descripcion], o.[Tipo Descripcion],
                        YEAR(DATEADD(MONTH, n.Num, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))) AS Año,
                        MONTH(DATEADD(MONTH, n.Num, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))) AS Mes,
                        FORMAT(DATEADD(MONTH, n.Num, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)), 'MMM yyyy') AS MesAño,
                        36.0 + n.Num AS NumMes,
                        MONTH(DATEADD(MONTH, n.Num, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))) AS MesCalendario,
                        o.MedCantidad  + o.b1Cantidad * (36.0 + n.Num - o.MedX) AS TendCantidad,
                        o.MedCosto     + o.b1Costo    * (36.0 + n.Num - o.MedX) AS TendCosto
                      FROM OLS o
                      CROSS JOIN (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) n(Num)
                    )
                    SELECT [Insumo Descripcion], [Tipo Descripcion], MesAño, Año, Mes,
                           'Histórico'  AS TipoDato,
                           ROUND(Cantidad, 2)     AS CantidadComprada,
                           ROUND(CostoUnitario, 2) AS CostoUnitario,
                           NULL AS PronCantidad, NULL AS PronCostoUnitario
                    FROM Historico
                    UNION ALL
                    SELECT f.[Insumo Descripcion], f.[Tipo Descripcion], f.MesAño, f.Año, f.Mes,
                           'Pronóstico' AS TipoDato,
                           NULL, NULL,
                           ROUND(CASE WHEN f.TendCantidad * ISNULL(ie.IECantidad, 1) < 0 THEN 0
                                      ELSE f.TendCantidad * ISNULL(ie.IECantidad, 1) END, 2),
                           ROUND(CASE WHEN f.TendCosto    * ISNULL(ie.IECosto,    1) < 0 THEN 0
                                      ELSE f.TendCosto    * ISNULL(ie.IECosto,    1) END, 2)
                    FROM Forecast f
                    LEFT JOIN IndiceEstacional ie
                      ON f.[Insumo Descripcion] = ie.[Insumo Descripcion] AND f.MesCalendario = ie.[Mes]
                    ORDER BY [Insumo Descripcion], Año, Mes
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Proyección de demanda (cantidad) y costo unitario para los 5 principales insumos por volumen de compras. Método: regresión lineal OLS sobre 36 meses históricos para capturar tendencia, ajustada por índice de estacionalidad mensual calculado como ratio real/tendencia promedio por mes. El resultado incluye histórico real y pronóstico ajustado para los próximos 12 meses.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAño",
                    CampoEjeY: "CantidadComprada",
                    CampoAgrupacion: "Insumo Descripcion",
                    Titulo: "Demanda Histórica y Proyectada — Principales Insumos de Compras",
                    ColorPrimario: "#06b6d4"
                )
            ),

            // ── INDICADORES MARCO SINCO ADPRO — 7 Conceptos de Control ──────────

            // Familia 1 — Desviación y control del baseline
            new(
                Pregunta: "IRA — ¿Cuánto se ha desviado el proyectado actual respecto al presupuestado original por proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Presupuestado,
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Proyectado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Presupuestado,0) AS Presupuestado,
                      ROUND(Proyectado,0) AS Proyectado,
                      ROUND(Proyectado-Presupuestado,0) AS Desviacion,
                      ROUND(100.0*(Proyectado-Presupuestado)/NULLIF(Presupuestado,0),2) AS IRA_Pct,
                      CASE
                        WHEN 100.0*(Proyectado-Presupuestado)/NULLIF(Presupuestado,0) > 10 THEN 'CRITICO'
                        WHEN 100.0*(Proyectado-Presupuestado)/NULLIF(Presupuestado,0) > 5  THEN 'ALERTA'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM Base
                    ORDER BY ABS(Proyectado-Presupuestado) DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Índice de Reproyección Acumulada (IRA) por proyecto: mide la desviación porcentual del costo proyectado vigente respecto al presupuesto baseline original. IRA > 5% → revisión de supuestos; IRA > 10% → comité extraordinario.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "IRA_Pct",
                    CampoAgrupacion: null,
                    Titulo: "IRA — Desviación Proyectado vs Baseline (%)",
                    ColorPrimario: "#ef4444"
                )
            ),

            new(
                Pregunta: "CDT — ¿Qué capítulos concentran la mayor parte de la desviación total del proyecto (análisis Pareto por capítulo)?",
                Sql: """
                    WITH DesviacionProyecto AS (
                      SELECT
                        cp.SkIdEmpresa,
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                          - SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS DesviacionTotal
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      GROUP BY cp.SkIdEmpresa
                    ),
                    Base AS (
                      SELECT
                        p.[Nombre Proyecto],
                        cap.[Capitulo Descripcion], cap.[Tipo Costo],
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Presupuestado,
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Proyectado,
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                          - SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS DesviacionCapitulo,
                        dp.DesviacionTotal
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[CapituloPresupuesto] cap ON cp.SkIdCapitulo=cap.SkIdCapitulo AND cp.SkIdEmpresa=cap.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      INNER JOIN DesviacionProyecto dp ON cp.SkIdEmpresa=dp.SkIdEmpresa
                      GROUP BY p.[Nombre Proyecto], cap.[Capitulo Descripcion], cap.[Tipo Costo], dp.DesviacionTotal
                      HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT TOP 20
                      [Nombre Proyecto], [Capitulo Descripcion], [Tipo Costo],
                      ROUND(Presupuestado,0) AS Presupuestado,
                      ROUND(Proyectado,0) AS Proyectado,
                      ROUND(DesviacionCapitulo,0) AS DesviacionCapitulo,
                      ROUND(100.0*DesviacionCapitulo/NULLIF(DesviacionTotal,0),2) AS CDT_Pct,
                      ROUND(100.0*SUM(DesviacionCapitulo) OVER (ORDER BY ABS(DesviacionCapitulo) DESC
                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
                            /NULLIF(SUM(ABS(DesviacionCapitulo)) OVER (),0),2) AS ParetoAcumulado
                    FROM Base
                    ORDER BY ABS(DesviacionCapitulo) DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Contribución a la Desviación Total (CDT) por capítulo: identifica los pocos capítulos que concentran el 70-80% del sobrecosto total. Aplicación del principio de Pareto para focalizar la acción gerencial.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Capitulo Descripcion",
                    CampoEjeY: "DesviacionCapitulo",
                    CampoAgrupacion: null,
                    Titulo: "CDT — Top 20 Capítulos por Contribución a la Desviación Total",
                    ColorPrimario: "#ef4444"
                )
            ),

            new(
                Pregunta: "VAC — ¿Cuánto se estima que costará adicional al presupuesto original y cuál es el costo final estimado (EAC) de cada proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Presupuestado,
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido,
                        SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Ejecutado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                        AND SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Presupuestado,0) AS Presupuestado,
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Ejecutado,0) AS Ejecutado,
                      ROUND(CASE WHEN Ejecutado=0 THEN NULL ELSE Invertido/Ejecutado END,4) AS CPI,
                      ROUND(CASE WHEN Ejecutado=0 THEN NULL
                            ELSE Presupuestado*(Invertido/NULLIF(Ejecutado,0)) END,0) AS EAC_Continuista,
                      ROUND(CASE WHEN Ejecutado=0 THEN NULL
                            ELSE Presupuestado - Presupuestado*(Invertido/NULLIF(Ejecutado,0)) END,0) AS VAC,
                      ROUND(CASE WHEN Ejecutado=0 THEN NULL
                            ELSE 100.0*(Presupuestado - Presupuestado*(Invertido/NULLIF(Ejecutado,0)))/NULLIF(Presupuestado,0) END,2) AS VAC_Pct,
                      CASE
                        WHEN 100.0*(Presupuestado - Presupuestado*(Invertido/NULLIF(Ejecutado,0)))/NULLIF(Presupuestado,0) < -8 THEN 'CRITICO'
                        WHEN 100.0*(Presupuestado - Presupuestado*(Invertido/NULLIF(Ejecutado,0)))/NULLIF(Presupuestado,0) < -3 THEN 'ALERTA'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM Base
                    ORDER BY VAC_Pct ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Estimado al Completar (EAC continuista = P × CPI) y Variación al Completar (VAC = P − EAC) por proyecto. VAC% < −8% requiere reforma formal del presupuesto. CPI = Ejecutado físico / Invertido.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "VAC_Pct",
                    CampoAgrupacion: null,
                    Titulo: "VAC% — Variación al Completar por Proyecto (negativo = sobrecosto)",
                    ColorPrimario: "#f43f5e"
                )
            ),

            // Familia 2 — Compromiso contractual y aseguramiento
            new(
                Pregunta: "%A — ¿Qué porcentaje del presupuesto proyectado ya está asegurado mediante compras o contratos firmados por proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Proyectado,
                        SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Asegurado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Proyectado,0) AS Proyectado,
                      ROUND(Asegurado,0) AS Asegurado,
                      ROUND(Proyectado-Asegurado,0) AS PendienteAsegurar,
                      ROUND(100.0*Asegurado/NULLIF(Proyectado,0),2) AS Aseguramiento_Pct,
                      CASE
                        WHEN 100.0*Asegurado/NULLIF(Proyectado,0) < 40 THEN 'ALERTA — Rezago en contratacion'
                        WHEN 100.0*Asegurado/NULLIF(Proyectado,0) > 95 THEN 'Margen minimo para imprevistos'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM Base
                    ORDER BY Aseguramiento_Pct ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Porcentaje de Aseguramiento (%A = Asegurado / Proyectado × 100). Por debajo del 40% indica rezago en contratación con riesgo de sobrecostos por compras spot. Por encima del 95% al cierre, deja poco margen para imprevistos.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "Aseguramiento_Pct",
                    CampoAgrupacion: null,
                    Titulo: "%A — Porcentaje de Aseguramiento por Proyecto",
                    ColorPrimario: "#6366f1"
                )
            ),

            new(
                Pregunta: "ICC — ¿Cuánto del dinero ya comprometido (asegurado) se ha convertido efectivamente en costo causado (invertido) por proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado],
                        SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Asegurado,
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado]
                      HAVING SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado],
                      ROUND(Asegurado,0) AS Asegurado,
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Asegurado-Invertido,0) AS CompromisoPendiente,
                      ROUND(100.0*Invertido/NULLIF(Asegurado,0),2) AS ICC_Pct,
                      CASE
                        WHEN 100.0*Invertido/NULLIF(Asegurado,0) < 50 THEN 'BAJO — Posible retraso de proveedor'
                        WHEN 100.0*Invertido/NULLIF(Asegurado,0) >= 90 THEN 'ALTO — Ejecucion fluida'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM Base
                    ORDER BY ICC_Pct ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Índice de Conversión Contractual (ICC = Invertido / Asegurado × 100). ICC bajo indica compromisos firmados sin ejecutar — posible retraso de proveedor. ICC crítico cuando está por debajo del avance físico consolidado del proyecto.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "ICC_Pct",
                    CampoAgrupacion: null,
                    Titulo: "ICC — Índice de Conversión Contractual por Proyecto (%)",
                    ColorPrimario: "#6366f1"
                )
            ),

            new(
                Pregunta: "RCC — ¿Qué proporción del aseguramiento está cubierta por órdenes de compra versus contratos de subcontrato por proyecto?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado],
                      ROUND(SUM(ISNULL(ea.[Total Entrada],0)),0) AS ValorComprado,
                      ROUND(SUM(ISNULL(c.[Valor Total],0)),0) AS ValorContratado,
                      ROUND(SUM(ISNULL(ea.[Total Entrada],0))+SUM(ISNULL(c.[Valor Total],0)),0) AS TotalAsegurado,
                      ROUND(100.0*SUM(ISNULL(ea.[Total Entrada],0))
                        /NULLIF(SUM(ISNULL(ea.[Total Entrada],0))+SUM(ISNULL(c.[Valor Total],0)),0),2) AS RCC_Pct
                    FROM [ADP_DTM_DIM].[Proyecto] p
                    LEFT JOIN [ADP_DTM_FACT].[EntradasAlmacen] ea
                      ON ea.SkIdProyecto=p.SkIdProyecto AND ea.SkIdEmpresa=p.SkIdEmpresa
                    LEFT JOIN [ADP_DTM_FACT].[Contrato] c
                      ON c.SkIdProyecto=p.SkIdProyecto AND c.SkIdEmpresa=p.SkIdEmpresa
                    WHERE p.[Estado]='En ejecucion'
                    GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado]
                    HAVING (SUM(ISNULL(ea.[Total Entrada],0))+SUM(ISNULL(c.[Valor Total],0))) > 0
                    ORDER BY TotalAsegurado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Ratio Compra/Contrato (RCC = Comprado / Total Asegurado × 100). Proyectos con RCC muy alto mantienen el riesgo de ejecución en el promotor; con RCC muy bajo transfieren riesgo pero pierden control sobre calidad y tiempos. Rango saludable para obra residencial: 30–50%.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "RCC_Pct",
                    CampoAgrupacion: null,
                    Titulo: "RCC — Ratio Compra vs Contrato en Aseguramiento (%)",
                    ColorPrimario: "#8b5cf6"
                )
            ),

            new(
                Pregunta: "%NA — ¿Cuánto del presupuesto de cada capítulo aún no tiene respaldo contractual ni orden de compra?",
                Sql: """
                    SELECT TOP 20
                      p.[Nombre Proyecto], p.[Estado],
                      cap.[Capitulo Descripcion], cap.[Tipo Costo],
                      ROUND(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Presupuestado,
                      ROUND(SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Asegurado,
                      ROUND(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                        - SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS PendienteAsegurar,
                      ROUND(100.0*(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                        - SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END))
                        /NULLIF(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0),2) AS NA_Pct
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[CapituloPresupuesto] cap ON cp.SkIdCapitulo=cap.SkIdCapitulo AND cp.SkIdEmpresa=cap.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                    WHERE p.[Estado]='En ejecucion'
                    GROUP BY p.[Nombre Proyecto], p.[Estado], cap.[Capitulo Descripcion], cap.[Tipo Costo]
                    HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                      AND (SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                         - SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)) > 0
                    ORDER BY PendienteAsegurar DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Porcentaje No Asegurado por Capítulo (%NA = (Presupuestado − Asegurado) / Presupuestado × 100). Capítulos con alto %NA cuando su avance físico supera el 30% son fuente segura de sobrecosto futuro. Indicador temprano de gestión contractual rezagada.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Capitulo Descripcion",
                    CampoEjeY: "PendienteAsegurar",
                    CampoAgrupacion: null,
                    Titulo: "%NA — Top 20 Capítulos sin Respaldo Contractual",
                    ColorPrimario: "#6366f1"
                )
            ),

            // Familia 3 — Eficiencia operativa en ejecución
            new(
                Pregunta: "RR/APU — ¿Cuánto valor ganado se obtiene por cada peso consumido en cada capítulo de obra (rendimiento relativo Ejecutado/Consumido)?",
                Sql: """
                    SELECT TOP 20
                      p.[Nombre Proyecto], p.[Estado],
                      cap.[Capitulo Descripcion], cap.[Tipo Costo],
                      ROUND(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Presupuestado,
                      ROUND(SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS ValorGanado,
                      ROUND(SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Consumido,
                      ROUND(
                        SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                        /NULLIF(SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0),4) AS RendimientoReal,
                      ROUND(
                        SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                        /NULLIF(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0),4) AS RendimientoVsPresupuesto,
                      CASE
                        WHEN SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) = 0 THEN 'Sin consumo'
                        WHEN SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                          / NULLIF(SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) < 0.90 THEN 'BAJO — Sobreconsumo'
                        WHEN SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                          / NULLIF(SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) > 1.10 THEN 'ALTO — APU conservador'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[CapituloPresupuesto] cap ON cp.SkIdCapitulo=cap.SkIdCapitulo AND cp.SkIdEmpresa=cap.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                    GROUP BY p.[Nombre Proyecto], p.[Estado], cap.[Capitulo Descripcion], cap.[Tipo Costo]
                    HAVING SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                      AND SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    ORDER BY RendimientoReal ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Rendimiento Real relativo (Valor Ganado / Consumido): ratio menor a 1.0 indica sobreconsumo de insumo respecto al avance físico obtenido. Umbral crítico: ratio < 0.90 sostenido por más de dos cortes. Equivale al CPI a nivel de capítulo.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Capitulo Descripcion",
                    CampoEjeY: "RendimientoReal",
                    CampoAgrupacion: null,
                    Titulo: "RR/APU — Rendimiento Ejecutado/Consumido por Capítulo (< 1 = sobreconsumo)",
                    ColorPrimario: "#14b8a6"
                )
            ),

            new(
                Pregunta: "IONC — ¿Cuánto del insumo causado contablemente (invertido) aún no ha sido consumido en obra por proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido,
                        SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Consumido
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE p.[Estado]='En ejecucion'
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Consumido,0) AS Consumido,
                      ROUND(Invertido-Consumido,0) AS IONC,
                      ROUND(100.0*(Invertido-Consumido)/NULLIF(Invertido,0),2) AS IONC_Pct,
                      CASE
                        WHEN 100.0*(Invertido-Consumido)/NULLIF(Invertido,0) > 25 THEN 'ALTO — Riesgo de acumulacion'
                        WHEN 100.0*(Invertido-Consumido)/NULLIF(Invertido,0) < 5  THEN 'BAJO — Posible desabasto'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM Base
                    ORDER BY IONC DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Inventario en Obra No Consumido (IONC = Invertido − Consumido). Valor alto indica acumulación de material en almacén sin aplicar — riesgo de pérdida, daño, robo o sobrecompra. Umbral: IONC no debe superar 15 días de consumo promedio proyectado.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "IONC",
                    CampoAgrupacion: null,
                    Titulo: "IONC — Invertido sin Consumir por Proyecto (Inventario en Obra)",
                    ColorPrimario: "#14b8a6"
                )
            ),

            new(
                Pregunta: "TQP — ¿A qué ritmo se está quemando el presupuesto mensualmente en cada proyecto activo este año (Tasa de Quemado Presupuestal)?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto], p.[Nombre Proyecto],
                      f.[MesAño], f.[Mes], f.[Año],
                      ROUND(SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS InvertidoMes,
                      ROUND(SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS EjecutadoMes,
                      ROUND(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS PresupuestoMes,
                      ROUND(
                        CASE WHEN SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)=0 THEN NULL
                        ELSE SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                           / SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                        END,4) AS TQP_Mes
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                    INNER JOIN [ADP_DTM_DIM].[Fecha] f ON cp.SkIdFecha=f.SkIdFecha
                    WHERE p.[Estado]='En ejecucion' AND f.[Año]=YEAR(GETDATE())
                    GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], f.[MesAño], f.[Mes], f.[Año]
                    HAVING (SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)
                         + SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END)) > 0
                    ORDER BY p.[Nombre Proyecto], f.[Mes]
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Tasa de Quemado Presupuestal mensual (TQP = Invertido / Ejecutado por mes). TQP > 1 indica que se gasta más de lo que se avanza físicamente — sobrecosto estructural. Proyectar TQP permite estimar sobrecosto total al cierre.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAño",
                    CampoEjeY: "TQP_Mes",
                    CampoAgrupacion: "Nombre Proyecto",
                    Titulo: "TQP — Tasa de Quemado Presupuestal Mensual (> 1 = sobrecosto)",
                    ColorPrimario: "#14b8a6"
                )
            ),

            new(
                Pregunta: "SPI — ¿Cuánto falta por invertir para completar cada proyecto (saldo por invertir)?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Presupuestado,
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Proyectado,
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido,
                        SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Ejecutado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE p.[Estado]='En ejecucion'
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Presupuestado,0) AS Presupuestado,
                      ROUND(Proyectado,0) AS Proyectado,
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Proyectado-Invertido,0) AS SaldoPorInvertir,
                      ROUND(100.0*Invertido/NULLIF(Proyectado,0),2) AS PctInvertido,
                      ROUND(100.0*Ejecutado/NULLIF(Proyectado,0),2) AS PctAvanceFisico
                    FROM Base
                    ORDER BY SaldoPorInvertir DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Saldo por Invertir (SPI = Proyectado − Invertido): cuánto dinero falta por causar para completar cada proyecto. Indicador fundamental para programación de caja y planeación de compras de los próximos períodos.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "SaldoPorInvertir",
                    CampoAgrupacion: null,
                    Titulo: "SPI — Saldo por Invertir para Completar cada Proyecto",
                    ColorPrimario: "#10b981"
                )
            ),

            new(
                Pregunta: "RCI — ¿Qué porcentaje del costo causado (invertido) ha sido efectivamente consumido en obra y qué parte permanece en almacén?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido,
                        SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Consumido
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE p.[Estado]='En ejecucion'
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Consumido,0) AS Consumido,
                      ROUND(Invertido-Consumido,0) AS EnAlmacen,
                      ROUND(100.0*Consumido/NULLIF(Invertido,0),2) AS RCI_Pct,
                      CASE
                        WHEN 100.0*Consumido/NULLIF(Invertido,0) < 80 THEN 'BAJO — Alta inmovilizacion'
                        WHEN 100.0*Consumido/NULLIF(Invertido,0) > 95 THEN 'ALTO — Riesgo desabasto'
                        ELSE 'Normal (80-92%)'
                      END AS Semaforo
                    FROM Base
                    ORDER BY RCI_Pct ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Ratio Consumo/Inversión (RCI = Consumido / Invertido × 100). RCI bajo indica alta inmovilización de capital en inventario. RCI alto indica operación esbelta o posible desabastecimiento. Rango saludable: 80–92%.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "RCI_Pct",
                    CampoAgrupacion: null,
                    Titulo: "RCI — Ratio Consumido/Invertido por Proyecto (% esbeltez de almacén)",
                    ColorPrimario: "#06b6d4"
                )
            ),

            // Familia 4 — Calidad de precio y gestión de compras
            new(
                Pregunta: "DUC — ¿En cuánto se desvía el valor real de los insumos comprados respecto al presupuestado por ítem?",
                Sql: """
                    WITH Presupuesto AS (
                      SELECT cp.SkIdInsumo, cp.SkIdEmpresa,
                        SUM(ISNULL(cp.[Valor Total],0)) AS ValorPresupuestado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE co.[Clase]='P' AND cp.SkIdInsumo IS NOT NULL
                      GROUP BY cp.SkIdInsumo, cp.SkIdEmpresa
                      HAVING SUM(ISNULL(cp.[Valor Total],0)) > 0
                    ),
                    Compras AS (
                      SELECT ea.SkIdInsumo, ea.SkIdEmpresa,
                        SUM(ISNULL(ea.[Total Entrada],0)) AS ValorComprado
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      GROUP BY ea.SkIdInsumo, ea.SkIdEmpresa
                      HAVING SUM(ISNULL(ea.[Total Entrada],0)) > 0
                    )
                    SELECT TOP 20
                      i.[Insumo Descripcion], i.[Tipo Descripcion], i.[Unidad],
                      ROUND(p.ValorPresupuestado,0) AS ValorPresupuestado,
                      ROUND(c.ValorComprado,0) AS ValorComprado,
                      ROUND(c.ValorComprado-p.ValorPresupuestado,0) AS Desviacion,
                      ROUND(100.0*(c.ValorComprado-p.ValorPresupuestado)/NULLIF(p.ValorPresupuestado,0),2) AS DUC_Pct,
                      CASE
                        WHEN ABS(100.0*(c.ValorComprado-p.ValorPresupuestado)/NULLIF(p.ValorPresupuestado,0)) > 8 THEN 'ALERTA — Renegociar'
                        ELSE 'Normal'
                      END AS Semaforo
                    FROM [ADP_DTM_DIM].[Insumo] i
                    INNER JOIN Presupuesto p ON i.SkIdInsumo=p.SkIdInsumo AND i.SkIdEmpresa=p.SkIdEmpresa
                    INNER JOIN Compras c ON i.SkIdInsumo=c.SkIdInsumo AND i.SkIdEmpresa=c.SkIdEmpresa
                    ORDER BY ABS(c.ValorComprado-p.ValorPresupuestado) DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Desviación Unitaria de Compra (DUC) por insumo: compara el valor real comprado vs el presupuestado. Permite identificar los 10-15 insumos para concentrar renegociación. Regla: DUC > 8% en insumos con peso > 3% del presupuesto activa plan de mitigación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Insumo Descripcion",
                    CampoEjeY: "DUC_Pct",
                    CampoAgrupacion: null,
                    Titulo: "DUC — Top 20 Insumos por Desviación de Precio Comprado vs Presupuestado (%)",
                    ColorPrimario: "#f97316"
                )
            ),

            new(
                Pregunta: "IRI — ¿Qué ítems presentan simultáneamente mayor desviación de precio y alto peso dentro del presupuesto (índice de riesgo de ítem)?",
                Sql: """
                    WITH PresupuestoTotal AS (
                      SELECT cp.SkIdEmpresa,
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS TotalPresupuesto
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      GROUP BY cp.SkIdEmpresa
                    ),
                    PorInsumo AS (
                      SELECT cp.SkIdInsumo, cp.SkIdEmpresa,
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS ValorPresupuestado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE co.[Clase]='P' AND cp.SkIdInsumo IS NOT NULL
                      GROUP BY cp.SkIdInsumo, cp.SkIdEmpresa
                      HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    ),
                    Compras AS (
                      SELECT ea.SkIdInsumo, ea.SkIdEmpresa,
                        SUM(ISNULL(ea.[Total Entrada],0)) AS ValorComprado
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      GROUP BY ea.SkIdInsumo, ea.SkIdEmpresa
                      HAVING SUM(ISNULL(ea.[Total Entrada],0)) > 0
                    )
                    SELECT TOP 20
                      i.[Insumo Descripcion], i.[Tipo Descripcion], i.[Unidad],
                      ROUND(pi.ValorPresupuestado,0) AS ValorPresupuestado,
                      ROUND(c.ValorComprado,0) AS ValorComprado,
                      ROUND(100.0*pi.ValorPresupuestado/NULLIF(pt.TotalPresupuesto,0),2) AS PesoEnPresupuesto_Pct,
                      ROUND(100.0*(c.ValorComprado-pi.ValorPresupuestado)/NULLIF(pi.ValorPresupuestado,0),2) AS DUC_Pct,
                      ROUND(
                        ABS(100.0*(c.ValorComprado-pi.ValorPresupuestado)/NULLIF(pi.ValorPresupuestado,0))
                        * (100.0*pi.ValorPresupuestado/NULLIF(pt.TotalPresupuesto,0)) / 100.0, 4) AS IRI
                    FROM [ADP_DTM_DIM].[Insumo] i
                    INNER JOIN PorInsumo pi ON i.SkIdInsumo=pi.SkIdInsumo AND i.SkIdEmpresa=pi.SkIdEmpresa
                    INNER JOIN Compras c ON i.SkIdInsumo=c.SkIdInsumo AND i.SkIdEmpresa=c.SkIdEmpresa
                    INNER JOIN PresupuestoTotal pt ON pi.SkIdEmpresa=pt.SkIdEmpresa
                    ORDER BY IRI DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Índice de Riesgo de Ítem (IRI = DUC% × Peso en Presupuesto): combina la desviación de precio y el peso del ítem. Los 10 ítems con mayor IRI concentran ~60-70% del riesgo total y requieren seguimiento semanal.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Insumo Descripcion",
                    CampoEjeY: "IRI",
                    CampoAgrupacion: null,
                    Titulo: "IRI — Top 20 Ítems por Índice de Riesgo (DUC × Peso Presupuestal)",
                    ColorPrimario: "#f97316"
                )
            ),

            new(
                Pregunta: "III — ¿Cómo ha evolucionado el precio unitario de los insumos más comprados entre compras sucesivas (inflación interna de insumo)?",
                Sql: """
                    WITH TopInsumos AS (
                      SELECT TOP 10 ea.SkIdInsumo, ea.SkIdEmpresa,
                        SUM(ISNULL(ea.[Total Entrada],0)) AS TotalComprado
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      GROUP BY ea.SkIdInsumo, ea.SkIdEmpresa
                      ORDER BY TotalComprado DESC
                    ),
                    PreciosMensuales AS (
                      SELECT
                        i.[Insumo Descripcion], i.[Tipo Descripcion], i.[Unidad],
                        f.[Año], f.[Mes], f.[MesAño],
                        CASE WHEN SUM(ISNULL(ea.[Entrada Cantidad],0))>0
                          THEN SUM(ISNULL(ea.[Total Entrada],0))/SUM(ISNULL(ea.[Entrada Cantidad],0))
                          ELSE NULL END AS PrecioUnitarioPromedio,
                        SUM(ISNULL(ea.[Total Entrada],0)) AS ValorMes
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      INNER JOIN TopInsumos ti ON ea.SkIdInsumo=ti.SkIdInsumo AND ea.SkIdEmpresa=ti.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[Insumo] i ON ea.SkIdInsumo=i.SkIdInsumo AND ea.SkIdEmpresa=i.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[Fecha] f ON ea.SkIdFechaEntrada=f.SkIdFecha
                      GROUP BY i.[Insumo Descripcion], i.[Tipo Descripcion], i.[Unidad], f.[Año], f.[Mes], f.[MesAño]
                    )
                    SELECT
                      [Insumo Descripcion], [Tipo Descripcion], [Unidad],
                      MesAño, Año, Mes,
                      ROUND(PrecioUnitarioPromedio,2) AS PrecioUnitario,
                      ROUND(ValorMes,0) AS ValorCompradoMes,
                      ROUND(MIN(PrecioUnitarioPromedio) OVER (PARTITION BY [Insumo Descripcion]),2) AS PrecioMinHistorico,
                      ROUND(MAX(PrecioUnitarioPromedio) OVER (PARTITION BY [Insumo Descripcion]),2) AS PrecioMaxHistorico,
                      ROUND(100.0*(PrecioUnitarioPromedio - FIRST_VALUE(PrecioUnitarioPromedio) OVER
                        (PARTITION BY [Insumo Descripcion] ORDER BY Año,Mes ROWS UNBOUNDED PRECEDING))
                        /NULLIF(FIRST_VALUE(PrecioUnitarioPromedio) OVER
                        (PARTITION BY [Insumo Descripcion] ORDER BY Año,Mes ROWS UNBOUNDED PRECEDING),0),2) AS III_Pct
                    FROM PreciosMensuales
                    WHERE PrecioUnitarioPromedio IS NOT NULL
                    ORDER BY [Insumo Descripcion], Año, Mes
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Inflación Interna de Insumo (III): variación acumulada del precio unitario promedio de compra respecto a la primera compra registrada. Insumos con III > 10% deben renegociarse por volumen futuro o buscar sustitutos.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAño",
                    CampoEjeY: "PrecioUnitario",
                    CampoAgrupacion: "Insumo Descripcion",
                    Titulo: "III — Evolución del Precio Unitario de los 10 Principales Insumos",
                    ColorPrimario: "#f59e0b"
                )
            ),

            // Familia 5 — Proyección y aprendizaje
            new(
                Pregunta: "IAFF — ¿El ritmo de causación contable (invertido) está alineado con el avance físico reportado en cada proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Presupuestado,
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido,
                        SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Ejecutado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE p.[Estado]='En ejecucion'
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                        AND SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Presupuestado,0) AS Presupuestado,
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Ejecutado,0) AS Ejecutado,
                      ROUND(100.0*Invertido/NULLIF(Presupuestado,0),2) AS PctFinanciero,
                      ROUND(100.0*Ejecutado/NULLIF(Presupuestado,0),2) AS PctFisico,
                      ROUND((Invertido/NULLIF(Presupuestado,0))/(Ejecutado/NULLIF(Presupuestado,0)),4) AS IAFF,
                      CASE
                        WHEN ABS((Invertido/NULLIF(Presupuestado,0))/(Ejecutado/NULLIF(Presupuestado,0))-1) > 0.15
                          AND Invertido > Ejecutado THEN 'ALERTA — Causacion adelantada'
                        WHEN ABS((Invertido/NULLIF(Presupuestado,0))/(Ejecutado/NULLIF(Presupuestado,0))-1) > 0.15
                          AND Invertido < Ejecutado THEN 'ALERTA — Rezago administrativo'
                        ELSE 'Alineado'
                      END AS Semaforo
                    FROM Base
                    ORDER BY ABS(IAFF-1) DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Índice de Alineación Financiero-Físico (IAFF = % Invertido / % Avance Físico). IAFF = 1.0: el dinero causado corresponde al avance real. IAFF > 1.15: causación adelantada (posible sobrecosto encubierto). IAFF < 0.85: rezago administrativo en causación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "IAFF",
                    CampoAgrupacion: null,
                    Titulo: "IAFF — Alineación Financiero-Físico por Proyecto (1.0 = alineado)",
                    ColorPrimario: "#a855f7"
                )
            ),

            new(
                Pregunta: "EPP — ¿Qué tan confiable es el proyectado como predictor del costo real invertido en cada proyecto?",
                Sql: """
                    WITH Base AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado],
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Proyectado,
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE p.[Estado]='En ejecucion'
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado]
                      HAVING SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                        AND SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado],
                      ROUND(Proyectado,0) AS Proyectado,
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(ABS(Proyectado-Invertido),0) AS ErrorAbsoluto,
                      ROUND(100.0*ABS(Proyectado-Invertido)/NULLIF(Proyectado,0),2) AS EPP_Pct,
                      CASE
                        WHEN 100.0*ABS(Proyectado-Invertido)/NULLIF(Proyectado,0) > 15 THEN 'CRITICO — Revisar metodologia'
                        WHEN 100.0*ABS(Proyectado-Invertido)/NULLIF(Proyectado,0) > 8  THEN 'ALERTA — Proyeccion poco confiable'
                        ELSE 'Confiable'
                      END AS Semaforo
                    FROM Base
                    ORDER BY EPP_Pct DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Error de Proyección (EPP = |Proyectado − Invertido| / Proyectado × 100). EPP > 15% sostenido requiere revisión metodológica del proceso de proyección del equipo. Mide qué tan bien el proyectado anticipa el costo real causado.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "EPP_Pct",
                    CampoAgrupacion: null,
                    Titulo: "EPP — Error de Proyección: Diferencia entre Proyectado e Invertido (%)",
                    ColorPrimario: "#a855f7"
                )
            ),

            new(
                Pregunta: "BIP — Comparativo del desempeño de todas las obras activas del portafolio en los siete conceptos de control (benchmark interno)",
                Sql: """
                    WITH Indicadores AS (
                      SELECT
                        p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad],
                        SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Presupuestado,
                        SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Proyectado,
                        SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Asegurado,
                        SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Invertido,
                        SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Consumido,
                        SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) AS Ejecutado
                      FROM [ADP_DTM_FACT].[ControlProyecto] cp
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                      INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                      WHERE p.[Estado]='En ejecucion'
                      GROUP BY p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                      HAVING SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END) > 0
                    )
                    SELECT
                      [Codigo Proyecto], [Nombre Proyecto], [Estado], [Ciudad],
                      ROUND(Presupuestado,0) AS Presupuestado,
                      ROUND(Proyectado,0) AS Proyectado,
                      ROUND(Asegurado,0) AS Asegurado,
                      ROUND(Invertido,0) AS Invertido,
                      ROUND(Consumido,0) AS Consumido,
                      ROUND(Ejecutado,0) AS Ejecutado,
                      ROUND(100.0*(Proyectado-Presupuestado)/NULLIF(Presupuestado,0),2) AS IRA_Pct,
                      ROUND(100.0*Asegurado/NULLIF(Proyectado,0),2) AS Aseguramiento_Pct,
                      ROUND(100.0*Invertido/NULLIF(Asegurado,0),2) AS ICC_Pct,
                      ROUND(100.0*Consumido/NULLIF(Invertido,0),2) AS RCI_Pct,
                      ROUND(CASE WHEN Ejecutado=0 THEN NULL ELSE Invertido/Ejecutado END,4) AS CPI
                    FROM Indicadores
                    ORDER BY CPI ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Benchmark Interno de Portafolio (BIP): cuadro comparativo de todos los proyectos activos con los cinco indicadores clave (IRA, %A, ICC, RCI, CPI). Permite identificar proyectos atípicos que requieren intervención gerencial o que contienen lecciones replicables.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "CPI",
                    CampoAgrupacion: null,
                    Titulo: "BIP — CPI por Proyecto Activo (benchmark portafolio)",
                    ColorPrimario: "#a855f7"
                )
            ),

            new(
                Pregunta: "GMC — ¿Cómo han evolucionado mes a mes los siete conceptos de control (presupuestado, proyectado, asegurado, invertido, consumido, ejecutado) en el portafolio activo?",
                Sql: """
                    SELECT
                      f.[Año], f.[Mes], f.[NombreMes], f.[MesAño],
                      ROUND(SUM(CASE WHEN co.[Clase]='P' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Presupuestado,
                      ROUND(SUM(CASE WHEN co.[Clase]='Y' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Proyectado,
                      ROUND(SUM(CASE WHEN co.[Clase] IN ('T','B') THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Asegurado,
                      ROUND(SUM(CASE WHEN co.[Clase]='I' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Invertido,
                      ROUND(SUM(CASE WHEN co.[Clase]='C' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Consumido,
                      ROUND(SUM(CASE WHEN co.[Clase]='J' THEN ISNULL(cp.[Valor Total],0) ELSE 0 END),0) AS Ejecutado
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p ON cp.SkIdProyecto=p.SkIdProyecto AND cp.SkIdEmpresa=p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co ON cp.SkIdClaseOrigen=co.SkIdClaseOrigen
                    INNER JOIN [ADP_DTM_DIM].[Fecha] f ON cp.SkIdFecha=f.SkIdFecha
                    WHERE p.[Estado]='En ejecucion'
                      AND f.[Año] >= YEAR(GETDATE())-1
                    GROUP BY f.[Año], f.[Mes], f.[NombreMes], f.[MesAño]
                    ORDER BY f.[Año], f.[Mes]
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Gradientes Mensuales por Concepto (GMC): evolución mensual de los seis conceptos de control en el portafolio activo. Cuando el gradiente del Invertido supera al Asegurado, hay ejecución sin respaldo contractual. Cuando el Consumido supera al Invertido, hay rezago de causación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAño",
                    CampoEjeY: "Invertido",
                    CampoAgrupacion: null,
                    Titulo: "GMC — Evolución Mensual de los Conceptos de Control del Portafolio",
                    ColorPrimario: "#a855f7"
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
                Pregunta: "¿De qué ciudades provienen los proveedores con mayor valor adjudicado?",
                Sql: """
                    SELECT
                      ISNULL([Tercero Ciudad], 'Sin ciudad') AS [Tercero Ciudad],
                      COUNT(DISTINCT [Tercero Nit]) AS TotalProveedores,
                      COUNT(*) AS TotalAdjudicaciones,
                      SUM(ISNULL([Adjudicacion Detalle Valor Total], 0)) AS ValorTotalAdjudicado
                    FROM [SRM_DTM_VFACT].[Adjudicaciones]
                    GROUP BY [Tercero Ciudad]
                    ORDER BY ValorTotalAdjudicado DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Distribución geográfica del valor adjudicado en licitaciones: ciudades de origen de los proveedores, número de proveedores distintos y total de adjudicaciones por ciudad.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Tercero Ciudad",
                    CampoEjeY: "ValorTotalAdjudicado",
                    CampoAgrupacion: null,
                    Titulo: "Valor Adjudicado por Ciudad del Proveedor",
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

            new(
                Pregunta: "¿Cuántos proyectos hay por ciudad y estado?",
                Sql: """
                    SELECT
                      p.[Ciudad],
                      p.[Estado],
                      COUNT(DISTINCT p.[Codigo Proyecto]) AS TotalProyectos,
                      STRING_AGG(p.[Nombre Proyecto], ', ') WITHIN GROUP (ORDER BY p.[Nombre Proyecto]) AS Proyectos
                    FROM [ADP_DTM_DIM].[Proyecto] p
                    WHERE p.[Ciudad] IS NOT NULL AND p.[Ciudad] <> ''
                    GROUP BY p.[Ciudad], p.[Estado]
                    ORDER BY TotalProyectos DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Distribución de proyectos por ciudad y estado, con el listado de cada uno.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Ciudad",
                    CampoEjeY: "TotalProyectos",
                    CampoAgrupacion: "Estado",
                    Titulo: "Proyectos por Ciudad y Estado",
                    ColorPrimario: "#3b82f6"
                )
            ),

            new(
                Pregunta: "¿Cuál es el costo ejecutado vs presupuestado agrupado por macroproyecto?",
                Sql: """
                    SELECT
                      p.[MacroProyecto],
                      p.[Empresa],
                      COUNT(DISTINCT p.[Codigo Proyecto]) AS TotalProyectos,
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
                    WHERE p.[MacroProyecto] IS NOT NULL AND p.[MacroProyecto] <> ''
                    GROUP BY p.[MacroProyecto], p.[Empresa]
                    HAVING SUM(CASE WHEN co.[Clase] = 'P' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) > 0
                    ORDER BY ValorConsumido DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Comparativa de presupuesto vs costo real consumido consolidado por macroproyecto, con porcentaje de ejecución y desviación.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "MacroProyecto",
                    CampoEjeY: "ValorConsumido",
                    CampoAgrupacion: null,
                    Titulo: "Presupuestado vs Consumido por Macroproyecto",
                    ColorPrimario: "#f59e0b"
                )
            ),

            new(
                Pregunta: "¿Cuál es el valor de actas cobradas por subcontratista en cada proyecto?",
                Sql: """
                    SELECT TOP 20
                      t.[Nombre] AS NombreSubcontratista,
                      t.[Nit],
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      COUNT(DISTINCT edc.[No. Contrato]) AS TotalContratos,
                      SUM(ISNULL(a.[Valor Total Acta], 0)) AS ValorTotalActas
                    FROM [ADP_DTM_FACT].[Acta] a
                    INNER JOIN [ADP_DTM_FACT].[Contrato] c
                      ON a.SkIdContrato = c.SkIdEspecificacionDeContratos
                      AND a.SkIdProyecto = c.SkIdProyecto
                      AND a.SkIdEmpresa = c.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[EspecificacionDeContratos] edc
                      ON c.SkIdEspecificacionDeContratos = edc.SkIdContrato AND c.SkIdEmpresa = edc.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Tercero] t
                      ON c.SkIdTercero = t.SkIdTercero
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON a.SkIdProyecto = p.SkIdProyecto AND a.SkIdEmpresa = p.SkIdEmpresa
                    GROUP BY
                      t.[Nombre], t.[Nit],
                      p.[Codigo Proyecto], p.[Nombre Proyecto]
                    HAVING SUM(ISNULL(a.[Valor Total Acta], 0)) > 0
                    ORDER BY ValorTotalActas DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 subcontratistas ordenados por valor total cobrado en actas, con el número de contratos asociados por proyecto.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "NombreSubcontratista",
                    CampoEjeY: "ValorTotalActas",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Subcontratistas por Valor de Actas Cobradas",
                    ColorPrimario: "#f97316"
                )
            ),

            new(
                Pregunta: "¿Cuál es el valor mensual de materiales ingresados al almacén este año?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      f.[Mes],
                      f.[NombreMes],
                      f.[MesAño],
                      COUNT(DISTINCT ea.[SkIdEspecificacionEntradasAlmacen]) AS NumeroEntradas,
                      SUM(ISNULL(ea.[Entrada Cantidad], 0)) AS CantidadTotal,
                      SUM(ISNULL(ea.[Total Entrada], 0)) AS ValorTotal
                    FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON ea.SkIdProyecto = p.SkIdProyecto AND ea.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[Fecha] f
                      ON ea.SkIdFechaEntrada = f.SkIdFecha
                    WHERE f.[Año] = YEAR(GETDATE())
                      AND p.[Estado] = 'En ejecucion'
                    GROUP BY
                      p.[Codigo Proyecto], p.[Nombre Proyecto],
                      f.[Mes], f.[NombreMes], f.[MesAño]
                    ORDER BY p.[Nombre Proyecto], f.[Mes]
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Valor mensual de materiales recibidos en almacén durante el año actual, para proyectos en ejecución.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Lineas,
                    CampoEjeX: "MesAño",
                    CampoEjeY: "ValorTotal",
                    CampoAgrupacion: "Nombre Proyecto",
                    Titulo: "Compras Mensuales de Materiales — Año Actual",
                    ColorPrimario: "#10b981"
                )
            ),

            new(
                Pregunta: "¿Cuál es el stock estimado de insumos (entradas menos salidas) en proyectos activos?",
                Sql: """
                    WITH Entradas AS (
                      SELECT ea.SkIdInsumo, ea.SkIdEmpresa,
                        SUM(ISNULL(ea.[Entrada Cantidad], 0)) AS TotalEntrada,
                        SUM(ISNULL(ea.[Total Entrada], 0))    AS ValorEntrada
                      FROM [ADP_DTM_FACT].[EntradasAlmacen] ea
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                        ON ea.SkIdProyecto = p.SkIdProyecto AND ea.SkIdEmpresa = p.SkIdEmpresa
                      WHERE p.[Estado] = 'En ejecucion'
                      GROUP BY ea.SkIdInsumo, ea.SkIdEmpresa
                    ),
                    Salidas AS (
                      SELECT sa.SkIdInsumo, sa.SkIdEmpresa,
                        SUM(ISNULL(sa.[Salida Cantidad], 0))      AS TotalSalida,
                        SUM(ISNULL(sa.[Salida Valor Total], 0))   AS ValorSalida
                      FROM [ADP_DTM_FACT].[SalidasAlmacen] sa
                      INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                        ON sa.SkIdProyecto = p.SkIdProyecto AND sa.SkIdEmpresa = p.SkIdEmpresa
                      WHERE p.[Estado] = 'En ejecucion'
                      GROUP BY sa.SkIdInsumo, sa.SkIdEmpresa
                    )
                    SELECT TOP 20
                      i.[Insumo Descripcion],
                      i.[Tipo Descripcion],
                      i.[Agrupacion Descripcion],
                      i.[Unidad],
                      ISNULL(e.TotalEntrada, 0)                              AS CantidadEntrada,
                      ISNULL(s.TotalSalida,  0)                              AS CantidadSalida,
                      ISNULL(e.TotalEntrada, 0) - ISNULL(s.TotalSalida, 0)  AS StockEstimado,
                      ISNULL(e.ValorEntrada, 0)                              AS ValorEntrada,
                      ISNULL(s.ValorSalida,  0)                              AS ValorSalida
                    FROM [ADP_DTM_DIM].[Insumo] i
                    LEFT JOIN Entradas e ON i.SkIdInsumo = e.SkIdInsumo AND i.SkIdEmpresa = e.SkIdEmpresa
                    LEFT JOIN Salidas  s ON i.SkIdInsumo = s.SkIdInsumo AND i.SkIdEmpresa = s.SkIdEmpresa
                    WHERE (ISNULL(e.ValorEntrada, 0) + ISNULL(s.ValorSalida, 0)) > 0
                      AND ISNULL(e.TotalEntrada, 0) - ISNULL(s.TotalSalida, 0) > 0
                    ORDER BY ISNULL(e.ValorEntrada, 0) DESC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Top 20 insumos con mayor stock estimado (entradas físicas menos salidas físicas) en proyectos activos, ordenados por valor de entrada.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Insumo Descripcion",
                    CampoEjeY: "StockEstimado",
                    CampoAgrupacion: null,
                    Titulo: "Top 20 Insumos: Stock Estimado en Proyectos Activos",
                    ColorPrimario: "#06b6d4"
                )
            ),

            new(
                Pregunta: "¿Cuál es el índice de desempeño del costo (CPI) de cada proyecto activo?",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      p.[Ciudad],
                      SUM(CASE WHEN co.[Clase] = 'J' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorEjecutado,
                      SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) AS ValorConsumido,
                      CASE
                        WHEN SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) = 0 THEN NULL
                        ELSE ROUND(
                          SUM(CASE WHEN co.[Clase] = 'J' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END)
                          / SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END), 4)
                      END AS CPI
                    FROM [ADP_DTM_FACT].[ControlProyecto] cp
                    INNER JOIN [ADP_DTM_DIM].[Proyecto] p
                      ON cp.SkIdProyecto = p.SkIdProyecto AND cp.SkIdEmpresa = p.SkIdEmpresa
                    INNER JOIN [ADP_DTM_DIM].[ControlClaseOrigen] co
                      ON cp.SkIdClaseOrigen = co.SkIdClaseOrigen
                    WHERE p.[Estado] = 'En ejecucion'
                    GROUP BY
                      p.[Codigo Proyecto], p.[Nombre Proyecto], p.[Estado], p.[Ciudad]
                    HAVING SUM(CASE WHEN co.[Clase] = 'C' THEN ISNULL(cp.[Valor Total], 0) ELSE 0 END) > 0
                    ORDER BY CPI ASC
                    """,
                TipoRespuesta: TipoRespuesta.TablaMasGrafico,
                ExplicacionTexto: "Índice de Desempeño del Costo (CPI = Ejecutado / Consumido) por proyecto activo. CPI < 1 indica sobrecosto; CPI > 1 indica eficiencia. Ordenado de menor a mayor para identificar los proyectos con mayor problema de costos.",
                Grafico: new ConfiguracionGrafico(
                    Tipo: TipoGrafico.Barras,
                    CampoEjeX: "Nombre Proyecto",
                    CampoEjeY: "CPI",
                    CampoAgrupacion: null,
                    Titulo: "CPI por Proyecto Activo (< 1 = Sobrecosto)",
                    ColorPrimario: "#ef4444"
                )
            ),

            new(
                Pregunta: "Listar los macroproyectos del sistema incluyendo la empresa y sus respectivos codigos",
                Sql: """
                    SELECT
                      p.[MacroProyecto],
                      p.[Empresa],
                      COUNT(DISTINCT p.[Codigo Proyecto]) AS TotalProyectos,
                      STRING_AGG(p.[Codigo Proyecto], ', ') WITHIN GROUP (ORDER BY p.[Codigo Proyecto]) AS CodigosProyectos
                    FROM [ADP_DTM_DIM].[Proyecto] p
                    WHERE p.[MacroProyecto] IS NOT NULL AND p.[MacroProyecto] <> ''
                    GROUP BY p.[MacroProyecto], p.[Empresa]
                    ORDER BY p.[Empresa], p.[MacroProyecto]
                    """,
                TipoRespuesta: TipoRespuesta.Tabla,
                ExplicacionTexto: "Listado de macroproyectos con su empresa, cantidad de proyectos asociados y los códigos de cada uno.",
                Grafico: null
            ),

            new(
                Pregunta: "Listar proyectos con su respectivo estado",
                Sql: """
                    SELECT
                      p.[Codigo Proyecto],
                      p.[Nombre Proyecto],
                      p.[Estado],
                      p.[Empresa],
                      p.[MacroProyecto]
                    FROM [ADP_DTM_DIM].[Proyecto] p
                    ORDER BY p.[Estado], p.[Nombre Proyecto]
                    """,
                TipoRespuesta: TipoRespuesta.Tabla,
                ExplicacionTexto: "Listado de todos los proyectos con su estado actual, empresa y macroproyecto al que pertenecen.",
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
