import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import {
  Database, Send, Loader2, ChevronDown, BarChart2,
  Table, FileText, AlertCircle, CheckCircle2, Zap,
  RefreshCw, Eye, EyeOff, Code, Server, Layers, Lightbulb,
  Bookmark, BookMarked, Clock, Trash2, PlayCircle, Search,
  Star, LayoutDashboard, MessageSquare, GripVertical, X,
  SlidersHorizontal, Filter, Sun, Moon, SunMoon
} from 'lucide-react'

// ─── Temas ────────────────────────────────────────────────────────────────────
const THEMES = [
  { id: 'dark',  label: 'Oscuro',  Icon: Moon,    iconClass: 'text-slate-400'  },
  { id: 'night', label: 'Noche',   Icon: SunMoon,  iconClass: 'text-indigo-400' },
  { id: 'day',   label: 'Light',   Icon: Sun,      iconClass: 'text-amber-400'  },
]

function useTheme() {
  const [theme, setTheme] = useState(() => localStorage.getItem('app-theme') || 'dark')
  useEffect(() => {
    const html = document.documentElement
    THEMES.forEach(t => html.classList.remove(`theme-${t.id}`))
    if (theme !== 'dark') html.classList.add(`theme-${theme}`)
    localStorage.setItem('app-theme', theme)
  }, [theme])
  const cycleTheme = () => {
    const idx = THEMES.findIndex(t => t.id === theme)
    setTheme(THEMES[(idx + 1) % THEMES.length].id)
  }
  return { theme, cycleTheme }
}
import {
  BarChart, Bar, LineChart, Line, PieChart, Pie, Cell,
  AreaChart, Area, ScatterChart, Scatter,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend,
  ResponsiveContainer
} from 'recharts'
import datamartApi from './services/api'

// ─── Colors ───────────────────────────────────────────────────────────────────
const CHART_COLORS = ['#3b82f6','#10b981','#f59e0b','#ef4444','#8b5cf6','#06b6d4','#f97316','#ec4899']

// ─── AI Models ────────────────────────────────────────────────────────────────
const AI_MODELS = [
  { id: 'claude-haiku-4-5-20251001', label: 'Haiku 4.5', desc: 'Rápido · Económico' },
  { id: 'claude-sonnet-4-20250514',  label: 'Sonnet 4',  desc: 'Balanceado · Recomendado' },
  { id: 'claude-sonnet-4-6',         label: 'Sonnet 4.6', desc: 'Más capaz · Mayor costo' },
]

// ─── Saved Queries (localStorage) ────────────────────────────────────────────
const STORAGE_KEY = 'datamart_saved_queries'
const loadSavedQueries = () => { try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]') } catch { return [] } }
const persistSavedQueries = (qs) => localStorage.setItem(STORAGE_KEY, JSON.stringify(qs))

// ─── Dashboards (localStorage) ───────────────────────────────────────────────
// Estructura: [{ id, nombre, paneles: [{ id, titulo, sql, tipoRespuesta, grafico }] }]
const DASHBOARDS_KEY = 'datamart_dashboards'
const newDashId = () => `dash_${Date.now()}_${Math.random().toString(36).slice(2,5)}`
const newPanelId = () => `pan_${Date.now()}_${Math.random().toString(36).slice(2,5)}`
const loadDashboards = () => { try { return JSON.parse(localStorage.getItem(DASHBOARDS_KEY) || '[]') } catch { return [] } }
const persistDashboards = (ds) => localStorage.setItem(DASHBOARDS_KEY, JSON.stringify(ds))

// ─── SINCO Logo SVG ───────────────────────────────────────────────────────────
function SincoLogo({ size = 26 }) {
  return (
    <svg width={size} height={Math.round(size * 0.694)} viewBox="0 0 36 25" fill="none">
      <path d="M13.1431 0C13.1431 0 7.70807 0.2821 10.8686 6.9947L14.3479 6.9834L21.6353 6.9906L26.8737 0H13.1431Z" fill="white"/>
      <path d="M9.22354 7.2338C6.18534 0.0527997 10.7606 0 10.7606 0L4.61004 0.0103002C0.278838 0.2889 -0.0456617 3.95 0.00423827 5.4449C0.0164383 5.8117 0.0954383 6.1728 0.234138 6.5126L7.22674 23.6536L12.9469 16.6202L9.22354 7.2338Z" fill="white"/>
      <path d="M24.5773 17.2383L21.1047 17.2495L14.2451 17.241L9.13477 24.1806L22.4204 24.1404C22.4204 24.1404 26.9012 24.0408 24.5773 17.2383Z" fill="white"/>
      <path d="M34.9398 17.319L28.3194 0.460938L22.6748 7.74014L26.0716 16.9236C29.1098 24.1046 24.0536 24.1574 24.0536 24.1574L30.6852 24.1471C34.3803 23.9094 35.1591 21.2098 35.2772 19.4938C35.3282 18.7529 35.2113 18.0103 34.9398 17.319Z" fill="white"/>
    </svg>
  )
}

// ─── Helpers de formato ───────────────────────────────────────────────────────
const fmtNum = v => {
  if (v === null || v === undefined || v === '') return v
  const n = Number(v)
  if (isNaN(n)) return v
  return new Intl.NumberFormat('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(n)
}
const fmtAxis = v => {
  const n = Number(v)
  if (isNaN(n)) return v
  if (Math.abs(n) >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (Math.abs(n) >= 1_000) return `${(n / 1_000).toFixed(0)}K`
  return new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(n)
}
const isNumericCol = (datos, key) => datos.slice(0, 5).every(r => r[key] === null || r[key] === undefined || !isNaN(Number(r[key])))

// ─── Detección de filtros soportados por una pregunta ─────────────────────────
// Regex espejos de Controllers.SqlUsaDimensionInsumo (backend)
const SQL_USA_PROYECTO = /\[?ADP_DTM_DIM\]?\.\[?Proyecto\]?\b/i
const SQL_USA_INSUMO   = /\[?ADP_DTM_DIM\]?\.\[?Insumo\]?\b/i

// Para saved/AI queries tenemos el SQL → detección exacta por regex
function detectarFiltrosDesdeSql(sql) {
  if (!sql) return []
  const soportados = []
  if (SQL_USA_PROYECTO.test(sql)) soportados.push('empresa', 'macroproyecto', 'proyecto', 'estado')
  if (SQL_USA_INSUMO.test(sql))   soportados.push('insumo')
  return soportados
}

// Para prebuilts (sugerencias) NO tenemos el SQL en el cliente → heurística
// por palabras clave. La inyección real de filtros es server-side (InjectarFiltrosPrebuilt),
// así que esta función solo sirve para la marca visual.
function detectarFiltrosDesdePregunta(pregunta) {
  if (!pregunta) return []
  const t = pregunta.toLowerCase()
  // Casi todos los prebuilts unen Proyecto (alias p); excepciones raras no
  // justifican complejidad extra. Default: soporta los 4 filtros sobre Proyecto.
  const soportados = ['empresa', 'macroproyecto', 'proyecto', 'estado']
  // Sigla/keyword matching para Insumo: menciones explícitas + siglas del set
  // "Familia 3/4/5" que sí tocan la dimensión Insumo (APU, DUC, III, IONC, IRI, RCI).
  if (/insumo|material|\bapu\b|\bduc\b|\biii\b|\bionc\b|\biri\b|\brci\b|rr\/apu/.test(t)) {
    soportados.push('insumo')
  }
  return soportados
}

// Dada una lista de tipos activos, devuelve los que NO aparecen en soportados
function filtrosActivosIgnorados(soportados, activos) {
  if (!activos?.length) return []
  return activos.filter(a => !soportados.includes(a))
}

// ─── Filter Support Badges ────────────────────────────────────────────────────
// Muestra 5 dots (uno por filtro en FILTER_CONFIG) con opacidad reducida para los
// no soportados. Si hay filtros activos ignorados, muestra un aviso ámbar al final.
function FilterSupportBadges({ soportados = [], activos = [], className = '' }) {
  const ignorados = filtrosActivosIgnorados(soportados, activos)
  return (
    <div className={`flex items-center gap-0.5 ${className}`}>
      {FILTER_CONFIG.map(({ tipo, label, dotBg }) => {
        const esSoportado = soportados.includes(tipo)
        const esActivoIgnorado = ignorados.includes(tipo)
        let title = `${label}: ${esSoportado ? 'aplica' : 'no aplica a esta consulta'}`
        if (esActivoIgnorado) title += ' (filtro activo — se ignora aquí)'
        return (
          <span
            key={tipo}
            title={title}
            className={`w-1.5 h-1.5 rounded-full ${dotBg} transition-opacity ${
              esSoportado ? 'opacity-90' : 'opacity-20'
            } ${esActivoIgnorado ? 'ring-1 ring-amber-400/60' : ''}`}
          />
        )
      })}
      {ignorados.length > 0 && (
        <span
          title={`Filtros activos que esta consulta ignora: ${ignorados.join(', ')}`}
          className="ml-1 text-[9px] text-amber-400 font-mono leading-none"
        >
          !{ignorados.length}
        </span>
      )}
    </div>
  )
}

// ─── Suggestion Card ──────────────────────────────────────────────────────────
function SuggestionCard({ s, color, uso, selectedDb, filtrosActivos = [], onSelect }) {
  const soportados = detectarFiltrosDesdePregunta(s.pregunta)
  const ignorados = filtrosActivosIgnorados(soportados, filtrosActivos)
  const hayIgnorados = ignorados.length > 0

  // Anillo ámbar sutil cuando la pregunta ignora filtros activos
  const warnRing = hayIgnorados ? 'ring-1 ring-amber-500/40' : ''

  return (
    <button onClick={onSelect} disabled={!selectedDb}
      className={`text-left rounded-xl border transition-all group flex flex-col gap-1.5 p-0 overflow-hidden ${warnRing} ${
        selectedDb
          ? 'bg-ink-800 border-gray-700 hover:border-gray-500 hover:bg-ink-700 cursor-pointer'
          : 'bg-ink-800/50 border-gray-800 cursor-not-allowed opacity-60'
      }`}>
      {/* Accent bar top */}
      <div className="h-0.5 w-full flex-shrink-0" style={{ background: color }} />
      <div className="px-3 pb-3 pt-1 flex flex-col gap-2 flex-1">
        {/* Pregunta */}
        <p className="text-sm text-gray-300 group-hover:text-white transition-colors leading-snug line-clamp-3">{s.pregunta}</p>
        {/* Footer de la card */}
        <div className="flex items-center justify-between mt-auto pt-1 gap-2">
          <span className="text-[10px] font-mono px-1.5 py-0.5 rounded flex-shrink-0" style={{ background: color + '22', color }}>
            {s.categoria}
          </span>
          <div className="flex items-center gap-2 flex-shrink-0">
            <FilterSupportBadges soportados={soportados} activos={filtrosActivos} />
            {uso > 0 && (
              <span className="text-[10px] font-mono text-amber-400 flex items-center gap-1">
                <span>🔥</span>{uso}x
              </span>
            )}
          </div>
        </div>
      </div>
    </button>
  )
}

// ─── Chart Renderer ───────────────────────────────────────────────────────────
function ChartDisplay({ datos, grafico }) {
  if (!datos?.length || !grafico) return null
  const { tipo, campoEjeX, campoEjeY, titulo, colorPrimario } = grafico
  const color = colorPrimario || '#3b82f6'

  const chartData = datos.map(row => ({
    ...row,
    [campoEjeX]: String(row[campoEjeX] ?? ''),
    [campoEjeY]: Number(row[campoEjeY] ?? 0)
  }))

  const commonProps = { data: chartData, margin: { top: 5, right: 30, left: 10, bottom: 5 } }

  const tooltipStyle = { background: '#111827', border: '1px solid #374151', borderRadius: 8, fontSize: 12 }
  const tooltipTextStyle = { color: '#f9fafb' }
  const tooltipFmt = (value) => [fmtNum(value), campoEjeY]

  const hasPctDesviacion = datos.length > 0 && 'PctDesviacion' in datos[0]
  const CustomTooltipPareto = ({ active, payload, label }) => {
    if (!active || !payload?.length) return null
    const row = payload[0]?.payload ?? {}
    return (
      <div style={tooltipStyle} className="px-3 py-2">
        <p style={{ color: '#f9fafb', marginBottom: 4, fontWeight: 600 }}>{label}</p>
        <p style={{ color: '#f9fafb' }}>{campoEjeY}: <span style={{ color: row[campoEjeY] >= 0 ? '#f87171' : '#34d399' }}>{fmtNum(row[campoEjeY])}</span></p>
        {row.PctDesviacion != null && (
          <p style={{ color: '#fbbf24' }}>% Desviación: {fmtNum(row.PctDesviacion)}%</p>
        )}
        {row.ParetoAcumuladoPct != null && (
          <p style={{ color: '#a78bfa' }}>Pareto acum.: {fmtNum(row.ParetoAcumuladoPct)}%</p>
        )}
      </div>
    )
  }

  const renderChart = () => {
    switch (tipo?.toLowerCase()) {
      case 'lineas':
        return (
          <LineChart {...commonProps}>
            <CartesianGrid strokeDasharray="3 3" stroke="#1f2937" />
            <XAxis dataKey={campoEjeX} stroke="#6b7280" tick={{ fontSize: 11 }} />
            <YAxis stroke="#6b7280" tick={{ fontSize: 11 }} tickFormatter={fmtAxis} width={60} />
            <Tooltip contentStyle={tooltipStyle} labelStyle={tooltipTextStyle} itemStyle={tooltipTextStyle} formatter={tooltipFmt} />
            <Legend />
            <Line type="monotone" dataKey={campoEjeY} stroke={color} strokeWidth={2} dot={{ r: 4 }} />
          </LineChart>
        )
      case 'torta':
      case 'pie':
        return (
          <PieChart margin={{ top: 5, right: 5, bottom: 5, left: 5 }}>
            <Pie data={chartData} dataKey={campoEjeY} nameKey={campoEjeX} cx="50%" cy="42%" outerRadius={85}>
              {chartData.map((_, i) => <Cell key={i} fill={CHART_COLORS[i % CHART_COLORS.length]} />)}
            </Pie>
            <Tooltip contentStyle={tooltipStyle} labelStyle={tooltipTextStyle} itemStyle={tooltipTextStyle} formatter={(value, name) => [fmtNum(value), name]} />
            <Legend wrapperStyle={{ fontSize: 12, paddingTop: 8 }} />
          </PieChart>
        )
      case 'area':
        return (
          <AreaChart {...commonProps}>
            <CartesianGrid strokeDasharray="3 3" stroke="#1f2937" />
            <XAxis dataKey={campoEjeX} stroke="#6b7280" tick={{ fontSize: 11 }} />
            <YAxis stroke="#6b7280" tick={{ fontSize: 11 }} tickFormatter={fmtAxis} width={60} />
            <Tooltip contentStyle={tooltipStyle} labelStyle={tooltipTextStyle} itemStyle={tooltipTextStyle} formatter={tooltipFmt} />
            <Legend />
            <Area type="monotone" dataKey={campoEjeY} stroke={color} fill={color + '33'} strokeWidth={2} />
          </AreaChart>
        )
      default: // Barras
        return (
          <BarChart {...commonProps}>
            <CartesianGrid strokeDasharray="3 3" stroke="#1f2937" />
            <XAxis dataKey={campoEjeX} stroke="#6b7280" tick={{ fontSize: 11 }} />
            <YAxis stroke="#6b7280" tick={{ fontSize: 11 }} tickFormatter={fmtAxis} width={60} />
            {hasPctDesviacion
              ? <Tooltip content={<CustomTooltipPareto />} />
              : <Tooltip contentStyle={tooltipStyle} labelStyle={tooltipTextStyle} itemStyle={tooltipTextStyle} formatter={tooltipFmt} />
            }
            <Legend />
            <Bar dataKey={campoEjeY} fill={color} radius={[4, 4, 0, 0]} />
          </BarChart>
        )
    }
  }

  const isPie = tipo?.toLowerCase() === 'torta' || tipo?.toLowerCase() === 'pie'

  return (
    <div className="mt-4">
      <p className="text-xs font-mono text-gray-400 mb-2 uppercase tracking-wider">{titulo}</p>
      <div className="bg-ink-800 rounded-xl p-4" style={{ height: isPie ? 420 : 300, overflow: 'hidden' }}>
        <ResponsiveContainer width="100%" height="100%">
          {renderChart()}
        </ResponsiveContainer>
      </div>
    </div>
  )
}

// ─── Data Table ───────────────────────────────────────────────────────────────
function DataTable({ datos, compact = false }) {
  if (!datos?.length) return null
  const keys = Object.keys(datos[0])
  const [page, setPage] = useState(0)
  const [sortCol, setSortCol] = useState(null)
  const [sortDir, setSortDir] = useState('asc')
  const PER_PAGE = compact ? 5 : 20

  const handleSort = (col) => {
    if (sortCol === col) setSortDir(d => d === 'asc' ? 'desc' : 'asc')
    else { setSortCol(col); setSortDir('asc') }
    setPage(0)
  }

  const sorted = sortCol
    ? [...datos].sort((a, b) => {
        const va = a[sortCol], vb = b[sortCol]
        if (va === null || va === undefined) return 1
        if (vb === null || vb === undefined) return -1
        const na = Number(va), nb = Number(vb)
        const cmp = !isNaN(na) && !isNaN(nb) ? na - nb : String(va).localeCompare(String(vb), 'es')
        return sortDir === 'asc' ? cmp : -cmp
      })
    : datos

  const totalPages = Math.ceil(sorted.length / PER_PAGE)
  const slice = sorted.slice(page * PER_PAGE, (page + 1) * PER_PAGE)

  return (
    <div className="mt-4">
      <div className="flex items-center justify-between mb-2">
        <span className="text-xs font-mono text-gray-400">{datos.length} filas · {keys.length} columnas</span>
        {totalPages > 1 && (
          <div className="flex gap-2 items-center">
            <button onClick={() => setPage(p => Math.max(0, p - 1))} disabled={page === 0}
              className="px-2 py-1 text-xs rounded bg-ink-700 text-gray-300 disabled:opacity-30 hover:bg-ink-600">‹</button>
            <span className="text-xs text-gray-400">{page + 1}/{totalPages}</span>
            <button onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))} disabled={page >= totalPages - 1}
              className="px-2 py-1 text-xs rounded bg-ink-700 text-gray-300 disabled:opacity-30 hover:bg-ink-600">›</button>
          </div>
        )}
      </div>
      <div className="overflow-x-auto rounded-xl border border-gray-800">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-ink-800 border-b border-gray-700">
              {keys.map(k => (
                <th key={k} onClick={() => handleSort(k)}
                  className="px-3 py-2 text-left text-gray-400 font-mono font-medium whitespace-nowrap cursor-pointer hover:text-gray-200 hover:bg-ink-700 select-none transition-colors">
                  <span className="flex items-center gap-1">
                    {k}
                    <span className="text-gray-600 text-xs">
                      {sortCol === k ? (sortDir === 'asc' ? '↑' : '↓') : '↕'}
                    </span>
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {slice.map((row, i) => (
              <tr key={i} className={`border-b border-gray-800/50 ${i % 2 === 0 ? 'bg-ink-900' : 'bg-ink-950'} hover:bg-ink-700/30 transition-colors`}>
                {keys.map(k => {
                  const v = row[k]
                  const isNum = v !== null && v !== undefined && !isNaN(Number(v)) && v !== ''
                  return (
                    <td key={k} className={`px-3 py-2 font-mono whitespace-nowrap max-w-[220px] truncate ${isNum ? 'text-right text-azure-400' : 'text-gray-300'}`}
                      title={String(v ?? '')}>
                      {v === null || v === undefined
                        ? <span className="text-gray-600 italic">null</span>
                        : isNum ? fmtNum(v) : String(v)}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

// ─── Message Bubble ───────────────────────────────────────────────────────────
function MessageBubble({ msg, devMode, pregunta, onSave, isSaved, usage, onFavorite, isFavorite }) {
  const [showSql, setShowSql] = useState(false)
  const { theme } = useTheme()
  const isDay = theme === 'day'
  const isUser = msg.role === 'user'

  if (isUser) {
    return (
      <div className="flex justify-end slide-up">
        <div className="max-w-[75%] bg-azure-600 text-white rounded-2xl rounded-tr-sm px-4 py-3 text-sm">
          {msg.content}
        </div>
      </div>
    )
  }

  if (msg.role === 'thinking') {
    return (
      <div className="flex items-center gap-3 slide-up">
        <div className="w-8 h-8 rounded-full bg-azure-600/20 border border-azure-500/30 flex items-center justify-center flex-shrink-0">
          <Zap size={14} className="text-azure-400" />
        </div>
        <div className="bg-ink-800 border border-gray-700 rounded-2xl rounded-tl-sm px-4 py-3 flex gap-2 items-center">
          <span className="typing-dot w-2 h-2 rounded-full bg-azure-400 inline-block" />
          <span className="typing-dot w-2 h-2 rounded-full bg-azure-400 inline-block" />
          <span className="typing-dot w-2 h-2 rounded-full bg-azure-400 inline-block" />
          <span className="text-xs text-gray-400 ml-2">Analizando con IA...</span>
        </div>
      </div>
    )
  }

  const { tipoRespuesta, explicacionTexto, sqlGenerado, datos, grafico, mensajeError, esPrebuilt, esSqlDirecto } = msg.response || {}
  const isError = tipoRespuesta === 'Error' || mensajeError

  const tipoIcon = {
    Tabla: <Table size={12} />,
    Grafico: <BarChart2 size={12} />,
    Texto: <FileText size={12} />,
    TablaMasGrafico: <Layers size={12} />,
    TablaMasTexto: <FileText size={12} />,
    Error: <AlertCircle size={12} />,
  }[tipoRespuesta] || <FileText size={12} />

  return (
    <div className="flex gap-3 slide-up">
      <div className="w-8 h-8 rounded-full bg-azure-600/20 border border-azure-500/30 flex items-center justify-center flex-shrink-0 mt-1">
        <Zap size={14} className="text-azure-400" />
      </div>
      <div className="flex-1 min-w-0">
        <div className={`rounded-2xl rounded-tl-sm px-4 py-4 border ${isError ? 'bg-red-950/30 border-red-800/40' : 'bg-ink-800 border-gray-700'}`}>

          {/* Tipo badge */}
          <div className="flex items-center gap-2 mb-3">
            <span className={`inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-mono ${isError ? 'bg-red-900/50 text-red-300' : 'bg-azure-600/20 text-azure-400'}`}>
              {tipoIcon} {tipoRespuesta}
            </span>
            {esPrebuilt && (
              <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-mono bg-emerald-900/40 text-emerald-400 border border-emerald-700/30" title="Consulta preconstruida — sin costo de IA">
                <Zap size={9} /> Instant
              </span>
            )}
            {esSqlDirecto && (
              <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-mono bg-orange-900/40 text-orange-400 border border-orange-700/30" title="SQL ejecutado directamente — sin IA">
                <Code size={9} /> SQL Directo
              </span>
            )}
            {datos?.length > 0 && (
              <span className="text-xs text-gray-500 font-mono">{datos.length} resultados</span>
            )}
          </div>

          {/* Texto explicativo */}
          {explicacionTexto && (
            <p className="text-sm text-gray-200 leading-relaxed mb-2">{explicacionTexto}</p>
          )}

          {/* Error */}
          {mensajeError && <p className="text-sm text-red-400 font-mono mt-2">{mensajeError}</p>}

          {/* Gráfico */}
          {(tipoRespuesta === 'Grafico' || tipoRespuesta === 'TablaMasGrafico') && datos?.length > 0 && grafico && (
            <ChartDisplay datos={datos} grafico={grafico} />
          )}

          {/* Tabla */}
          {(tipoRespuesta === 'Tabla' || tipoRespuesta === 'TablaMasGrafico' || tipoRespuesta === 'TablaMasTexto') && datos?.length > 0 && (
            <DataTable datos={datos} />
          )}

          {/* Sin resultados */}
          {(tipoRespuesta === 'Tabla' || tipoRespuesta === 'TablaMasGrafico' || tipoRespuesta === 'TablaMasTexto' || tipoRespuesta === 'Grafico') && datos?.length === 0 && (
            <div className="mt-3 flex items-center gap-2 text-xs text-gray-500 font-mono bg-ink-900 border border-gray-800 rounded-lg px-3 py-2">
              <Table size={12} />
              No se encontraron registros para esta consulta.
            </div>
          )}

          {/* SQL — solo en modo desarrollador */}
          {devMode && sqlGenerado && (
            <div className="mt-3">
              <button
                onClick={() => setShowSql(v => !v)}
                className="flex items-center gap-1.5 text-xs text-gray-500 hover:text-gray-300 transition-colors font-mono"
              >
                <Code size={11} />
                {showSql ? 'Ocultar' : 'Ver'} SQL generado
                {showSql ? <EyeOff size={11} /> : <Eye size={11} />}
              </button>
              {showSql && (
                <pre className={`mt-2 p-3 bg-ink-950 border border-gray-800 rounded-lg text-xs font-mono overflow-x-auto whitespace-pre-wrap ${isDay ? 'text-emerald-700' : 'text-emerald-400'}`}>
                  {sqlGenerado}
                </pre>
              )}
            </div>
          )}

          {/* Footer: costo IA + Guardar consulta */}
          {(usage?.costoUsd > 0 || (sqlGenerado && !isError && pregunta && (onSave || isSaved))) && (
            <div className="mt-3 pt-3 border-t border-gray-800 flex items-center justify-end gap-3">
              {usage?.costoUsd > 0 && (
                <span className="text-[11px] font-mono text-gray-500" title={`Modelo: ${usage.modelo}`}>
                  <span className="text-gray-600">{((usage.tokensEntrada ?? 0) + (usage.tokensSalida ?? 0)).toLocaleString('en-US')} tokens</span>
                  <span className="text-gray-700 mx-1">·</span>
                  costo IA: <span className="text-emerald-400/80">~${usage.costoUsd.toFixed(5)}</span> USD
                </span>
              )}
              {sqlGenerado && !isError && onFavorite && (
                <button
                  onClick={onFavorite}
                  title={isFavorite ? 'Ya está en el Dashboard' : 'Agregar al Dashboard de favoritos'}
                  className={`flex items-center gap-1.5 text-xs px-2.5 py-1.5 rounded-lg border transition-all font-mono ${
                    isFavorite
                      ? isDay
                        ? 'bg-amber-50 border-amber-300 text-amber-700 cursor-default'
                        : 'bg-amber-900/30 border-amber-600/40 text-amber-400 cursor-default'
                      : 'bg-ink-900 border-gray-700 text-gray-500 hover:text-amber-400 hover:border-amber-500/40 hover:bg-amber-900/10'
                  }`}
                >
                  <Star size={11} className={isFavorite ? 'fill-amber-400' : ''} />
                  {isFavorite ? 'En Dashboard' : 'Favorito'}
                </button>
              )}
              {sqlGenerado && !isError && pregunta && (onSave || isSaved) && (
                <button
                  onClick={onSave}
                  title={isSaved ? 'Consulta ya guardada en historial' : 'Guardar pregunta y SQL para reutilizar sin costo de IA'}
                  className={`flex items-center gap-1.5 text-xs px-2.5 py-1.5 rounded-lg border transition-all font-mono ${
                    isSaved
                      ? isDay
                        ? 'bg-emerald-50 border-emerald-300 text-emerald-700 cursor-default'
                        : 'bg-emerald-900/30 border-emerald-700/40 text-emerald-400 cursor-default'
                      : 'bg-ink-900 border-gray-700 text-gray-500 hover:text-amber-400 hover:border-amber-500/40 hover:bg-amber-900/10'
                  }`}
                >
                  {isSaved ? <BookMarked size={11} /> : <Bookmark size={11} />}
                  {isSaved ? 'Guardada en historial' : 'Guardar consulta'}
                </button>
              )}
            </div>
          )}

          {/* Badge de origen historial */}
          {msg.fromHistory && (
            <div className="mt-3 pt-3 border-t border-gray-800 flex items-center gap-1.5">
              <PlayCircle size={11} className="text-purple-400" />
              <span className="text-xs font-mono text-purple-400">Ejecutado desde historial · sin costo IA</span>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ─── Favorite Modal ───────────────────────────────────────────────────────────
const NUEVO_DASH = '__nuevo__'
function FavoriteModal({ titulo, dashboards, onConfirm, onCancel }) {
  const [panelTitulo, setPanelTitulo] = useState(titulo)
  const [dashId, setDashId] = useState(dashboards[0]?.id ?? NUEVO_DASH)
  const [nuevoDashNombre, setNuevoDashNombre] = useState('')
  const inputRef = useRef(null)
  useEffect(() => { setTimeout(() => inputRef.current?.focus(), 50) }, [])

  const esNuevo = dashId === NUEVO_DASH
  const puedeConfirmar = panelTitulo.trim() && (esNuevo ? nuevoDashNombre.trim() : true)

  const handleConfirm = () => {
    if (!puedeConfirmar) return
    onConfirm({ panelTitulo: panelTitulo.trim() || titulo, dashId: esNuevo ? null : dashId, nuevoDashNombre: esNuevo ? nuevoDashNombre.trim() : null })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm" onClick={onCancel}>
      <div className="bg-ink-800 border border-gray-700 rounded-2xl shadow-2xl w-full mx-4 p-6" style={{ maxWidth: '35vw', minWidth: 340 }} onClick={e => e.stopPropagation()}>
        <div className="flex items-center gap-2 mb-4">
          <Star size={16} className="text-amber-400 fill-amber-400" />
          <span className="text-sm font-semibold text-white">Agregar al Dashboard</span>
        </div>

        <p className="text-xs text-gray-500 mb-1.5">Título del panel</p>
        <input
          ref={inputRef}
          value={panelTitulo}
          onChange={e => setPanelTitulo(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && puedeConfirmar) handleConfirm(); if (e.key === 'Escape') onCancel() }}
          className="w-full bg-ink-900 border border-gray-600 focus:border-amber-500/60 rounded-lg px-3 py-2 text-sm text-gray-200 outline-none mb-3"
          placeholder="Ej: Costo vs Presupuesto"
        />

        <p className="text-xs text-gray-500 mb-1.5">Dashboard destino</p>
        <select
          value={dashId}
          onChange={e => setDashId(e.target.value)}
          className="w-full bg-ink-900 border border-gray-600 focus:border-amber-500/60 rounded-lg px-3 py-2 text-sm text-gray-200 outline-none mb-3"
        >
          {dashboards.map(d => (
            <option key={d.id} value={d.id}>{d.nombre} ({d.paneles.length} paneles)</option>
          ))}
          <option value={NUEVO_DASH}>➕ Crear nuevo dashboard...</option>
        </select>

        {esNuevo && (
          <input
            autoFocus
            value={nuevoDashNombre}
            onChange={e => setNuevoDashNombre(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter' && puedeConfirmar) handleConfirm() }}
            className="w-full bg-ink-900 border border-amber-600/40 focus:border-amber-500/60 rounded-lg px-3 py-2 text-sm text-gray-200 outline-none mb-3"
            placeholder="Nombre del nuevo dashboard..."
          />
        )}

        <div className="flex justify-end gap-2 mt-1">
          <button onClick={onCancel} className="px-3 py-1.5 rounded-lg text-xs text-gray-500 hover:text-gray-300 border border-gray-700 hover:border-gray-600 transition-colors">Cancelar</button>
          <button onClick={handleConfirm} disabled={!puedeConfirmar} className="px-3 py-1.5 rounded-lg text-xs bg-amber-600 hover:bg-amber-500 disabled:opacity-40 disabled:cursor-not-allowed text-white font-semibold transition-colors">Agregar</button>
        </div>
      </div>
    </div>
  )
}

// ─── Dashboard Panel ──────────────────────────────────────────────────────────
function DashboardPanel({ fav, database, appliedFilters, onToggleGlobal, onRemove, onDragStart, onDragOver, onDrop, isDragOver }) {
  const [datos, setDatos] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const db = database?.nombre ?? database

  const ejecutar = useCallback(async () => {
    if (!db) return
    setLoading(true); setError(null)
    try {
      // Determinar fuente de filtros
      const source = fav.aceptaFiltrosGlobales !== false ? (appliedFilters ?? {}) : (fav.filtrosFijos ?? {})
      const filters = Object.fromEntries(Object.entries(source).filter(([, v]) => Array.isArray(v) && v.length > 0))
      const hasFilters = Object.keys(filters).length > 0

      if (fav.esPrebuilt && fav.prebuiltKey) {
        // Prebuilt: re-ejecutar via /api/analyze con filtros limpios
        const resp = await datamartApi.analyze({
          database: db, pregunta: fav.prebuiltKey,
          schema: [], vistas: [], historialContexto: null, modelo: null,
          contextoVariables: hasFilters ? filters : null,
        })
        if (resp.tipoRespuesta === 'Error') setError(resp.mensajeError ?? 'Error al ejecutar')
        else setDatos(resp.datos ?? [])
      } else if (hasFilters) {
        // AI query + filtros: inyectar en sqlBase
        const resp = await datamartApi.executeWithFilters(db, fav.sqlBase ?? fav.sql, filters)
        if (resp.exitoso) setDatos(resp.datos ?? [])
        else setError(resp.error ?? 'Error al ejecutar')
      } else {
        // Sin filtros: ejecutar directo
        const res = await datamartApi.executeQuery(db, fav.sql)
        if (res.exitoso) setDatos(res.datos ?? [])
        else setError(res.error ?? 'Error al ejecutar')
      }
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }, [db, fav, appliedFilters])

  useEffect(() => { ejecutar() }, [ejecutar])

  const tipoResp = fav.tipoRespuesta
  const showChart = (tipoResp === 'Grafico' || tipoResp === 'TablaMasGrafico') && fav.grafico
  const showTable = tipoResp === 'Tabla' || tipoResp === 'TablaMasGrafico' || tipoResp === 'TablaMasTexto'
  const globalActive = fav.aceptaFiltrosGlobales !== false
  const appliedActivos = Object.entries(appliedFilters ?? {})
    .filter(([, v]) => Array.isArray(v) && v.length > 0)
    .map(([k]) => k)
  const activeGlobalCount = globalActive ? appliedActivos.length : 0
  // Detectar filtros soportados por este panel
  const soportados = fav.esPrebuilt && fav.prebuiltKey
    ? detectarFiltrosDesdePregunta(fav.prebuiltKey)
    : detectarFiltrosDesdeSql(fav.sqlBase ?? fav.sql)
  // Solo resaltar si el panel acepta filtros globales y algunos no aplican
  const hayIgnorados = globalActive && filtrosActivosIgnorados(soportados, appliedActivos).length > 0

  return (
    <div
      draggable
      onDragStart={onDragStart}
      onDragOver={e => { e.preventDefault(); onDragOver() }}
      onDrop={onDrop}
      className={`bg-ink-800 border rounded-2xl overflow-hidden flex flex-col transition-all ${
        isDragOver
          ? 'border-amber-500/60 shadow-lg shadow-amber-900/20'
          : hayIgnorados
            ? 'border-amber-500/30'
            : 'border-gray-700'
      }`}
    >
      {/* Panel header */}
      <div className="flex items-center gap-2 px-4 py-3 border-b border-gray-700 bg-ink-900/40">
        <div className="cursor-grab active:cursor-grabbing text-gray-600 hover:text-gray-400 transition-colors">
          <GripVertical size={14} />
        </div>
        <Star size={11} className="text-amber-400 fill-amber-400 flex-shrink-0" />
        <span className="text-xs font-semibold text-gray-200 flex-1 truncate">{fav.titulo}</span>
        {/* Badges de compatibilidad (siempre visibles) */}
        <FilterSupportBadges
          soportados={soportados}
          activos={globalActive ? appliedActivos : []}
          className="mr-0.5"
        />
        {/* Toggle filtros globales */}
        <button
          onClick={onToggleGlobal}
          title={globalActive ? 'Usando filtros del dashboard — click para desactivar' : 'Activar filtros del dashboard'}
          className={`relative p-1 rounded transition-colors ${globalActive ? 'text-amber-400' : 'text-gray-600 hover:text-gray-400'}`}
        >
          <Filter size={12} />
          {activeGlobalCount > 0 && (
            <span className="absolute -top-0.5 -right-0.5 min-w-[10px] h-[10px] bg-amber-500 rounded-full text-[7px] text-white font-bold flex items-center justify-center px-0.5">
              {activeGlobalCount}
            </span>
          )}
        </button>
        <button onClick={ejecutar} title="Reejecutar" className="p-1 rounded text-gray-600 hover:text-azure-400 transition-colors">
          <RefreshCw size={12} className={loading ? 'animate-spin' : ''} />
        </button>
        <button onClick={onRemove} title="Quitar del dashboard" className="p-1 rounded text-gray-600 hover:text-red-400 transition-colors">
          <X size={12} />
        </button>
      </div>

      {/* Panel body */}
      <div className="p-4 flex-1 min-h-0">
        {loading && (
          <div className="flex items-center justify-center h-32 gap-2">
            <Loader2 size={16} className="animate-spin text-azure-400" />
            <span className="text-xs text-gray-500 font-mono">Cargando...</span>
          </div>
        )}
        {!loading && error && (
          <div className="flex items-center gap-2 text-xs text-red-400 font-mono bg-red-950/30 border border-red-800/40 rounded-lg px-3 py-2">
            <AlertCircle size={12} /> {error}
          </div>
        )}
        {!loading && !error && datos?.length === 0 && (
          <div className="flex items-center justify-center h-20 text-xs text-gray-500 font-mono gap-2">
            <Table size={12} /> Sin resultados para este período
          </div>
        )}
        {!loading && !error && datos?.length > 0 && (
          <>
            {showChart && <ChartDisplay datos={datos} grafico={fav.grafico} />}
            {showTable && <DataTable datos={datos} compact />}
          </>
        )}
      </div>
    </div>
  )
}

// ─── Dashboard View ───────────────────────────────────────────────────────────
const EMPTY_DASH_FILTERS = { empresa: [], proyecto: [], macroproyecto: [], estado: [], insumo: [] }

function Dashboard({ dashboards, setDashboards, database, onGoConsulta }) {
  const [activeDashId, setActiveDashId] = useState(() => dashboards[0]?.id ?? null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [dragIdx, setDragIdx] = useState(null)
  const [dragOverIdx, setDragOverIdx] = useState(null)
  const [dropdownOpen, setDropdownOpen] = useState(false)
  const [renamingId, setRenamingId] = useState(null)
  const [renameVal, setRenameVal] = useState('')
  const dropRef = useRef(null)
  // Filtros del dashboard: pending = lo que el usuario está seleccionando,
  // applied = lo que se aplica a los paneles (solo cambia al dar Aplicar)
  const [pendingFilters, setPendingFilters] = useState(EMPTY_DASH_FILTERS)
  const [appliedFilters, setAppliedFilters] = useState(EMPTY_DASH_FILTERS)

  // Si se crea un nuevo dashboard desde afuera, seleccionarlo
  useEffect(() => {
    if (!activeDashId && dashboards.length > 0) setActiveDashId(dashboards[0].id)
  }, [dashboards, activeDashId])

  // Cerrar dropdown al clic fuera
  useEffect(() => {
    const handler = (e) => { if (dropRef.current && !dropRef.current.contains(e.target)) setDropdownOpen(false) }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  const activeDash = dashboards.find(d => d.id === activeDashId) ?? dashboards[0] ?? null
  const paneles = activeDash?.paneles ?? []

  const updateDashboards = (next) => { setDashboards(next); persistDashboards(next) }

  const handleRemovePanel = (panelId) => {
    const next = dashboards.map(d => d.id === activeDash.id ? { ...d, paneles: d.paneles.filter(p => p.id !== panelId) } : d)
    updateDashboards(next)
  }

  const handleDrop = (targetIdx) => {
    if (dragIdx === null || dragIdx === targetIdx) return
    const newPaneles = [...paneles]
    const [moved] = newPaneles.splice(dragIdx, 1)
    newPaneles.splice(targetIdx, 0, moved)
    const next = dashboards.map(d => d.id === activeDash.id ? { ...d, paneles: newPaneles } : d)
    updateDashboards(next)
    setDragIdx(null); setDragOverIdx(null)
  }

  const handleDeleteDashboard = (id) => {
    const next = dashboards.filter(d => d.id !== id)
    updateDashboards(next)
    if (activeDashId === id) setActiveDashId(next[0]?.id ?? null)
    setDropdownOpen(false)
  }

  const handleStartRename = (d) => { setRenamingId(d.id); setRenameVal(d.nombre); setDropdownOpen(false) }
  const handleRename = () => {
    if (!renameVal.trim()) return
    updateDashboards(dashboards.map(d => d.id === renamingId ? { ...d, nombre: renameVal.trim() } : d))
    setRenamingId(null)
  }

  const handleNewDashboard = () => {
    const nombre = `Dashboard ${dashboards.length + 1}`
    const id = newDashId()
    const next = [...dashboards, { id, nombre, paneles: [] }]
    updateDashboards(next)
    setActiveDashId(id)
    setDropdownOpen(false)
  }

  const totalPaneles = dashboards.reduce((s, d) => s + d.paneles.length, 0)

  const handleToggleGlobal = (panelId) => {
    const next = dashboards.map(d => d.id === activeDash?.id
      ? { ...d, paneles: d.paneles.map(p => p.id === panelId ? { ...p, aceptaFiltrosGlobales: p.aceptaFiltrosGlobales === false } : p) }
      : d)
    updateDashboards(next)
  }

  const handleDashFilterChange = (tipo, values) => setPendingFilters(prev => {
    const next = { ...prev, [tipo]: values }
    const children = FILTER_CHILDREN[tipo] || []
    children.forEach(child => { next[child] = [] })
    return next
  })
  const handleDashFilterApply = () => setAppliedFilters({ ...pendingFilters })
  const handleDashFilterClear = () => { setPendingFilters(EMPTY_DASH_FILTERS); setAppliedFilters(EMPTY_DASH_FILTERS) }

  const activeFilterCount = Object.values(appliedFilters).filter(v => v.length > 0).length

  return (
    <div className="flex-1 overflow-y-auto p-6">
      {/* Dashboard header */}
      <div className="flex items-center justify-between mb-6 flex-wrap gap-3">
        <div className="flex items-center gap-3">
          <LayoutDashboard size={18} className="text-amber-400" />

          {/* Dropdown selector */}
          <div className="relative" ref={dropRef}>
            <button
              onClick={() => setDropdownOpen(v => !v)}
              className="flex items-center gap-2 bg-ink-800 border border-gray-600 hover:border-amber-500/50 rounded-lg px-3 py-1.5 text-sm font-semibold text-white transition-colors min-w-[160px]"
            >
              <span className="flex-1 text-left truncate">{activeDash?.nombre ?? 'Sin dashboards'}</span>
              <ChevronDown size={13} className={`text-gray-400 transition-transform flex-shrink-0 ${dropdownOpen ? 'rotate-180' : ''}`} />
            </button>

            {dropdownOpen && (
              <div className="absolute left-0 top-full mt-1 w-64 bg-ink-800 border border-gray-700 rounded-xl shadow-2xl z-50 overflow-hidden">
                <div className="p-1.5 max-h-60 overflow-y-auto">
                  {dashboards.map(d => (
                    <div key={d.id} className={`flex items-center gap-1 rounded-lg px-2 py-1.5 group transition-colors ${d.id === activeDashId ? 'bg-amber-900/30' : 'hover:bg-ink-700'}`}>
                      {renamingId === d.id ? (
                        <input autoFocus value={renameVal} onChange={e => setRenameVal(e.target.value)}
                          onKeyDown={e => { if (e.key === 'Enter') handleRename(); if (e.key === 'Escape') setRenamingId(null) }}
                          onBlur={handleRename}
                          className="flex-1 bg-ink-900 border border-amber-500/40 rounded px-2 py-0.5 text-xs text-gray-200 outline-none" />
                      ) : (
                        <button className="flex-1 text-left text-sm text-gray-200 truncate" onClick={() => { setActiveDashId(d.id); setDropdownOpen(false) }}>
                          {d.id === activeDashId && <Star size={9} className="inline fill-amber-400 text-amber-400 mr-1.5 mb-0.5" />}
                          {d.nombre}
                          <span className="text-xs text-gray-600 ml-1.5">({d.paneles.length})</span>
                        </button>
                      )}
                      <button onClick={() => handleStartRename(d)} title="Renombrar" className="p-1 text-gray-600 hover:text-gray-300 opacity-0 group-hover:opacity-100 transition-all">
                        <FileText size={11} />
                      </button>
                      <button onClick={() => handleDeleteDashboard(d.id)} title="Eliminar dashboard" className="p-1 text-gray-600 hover:text-red-400 opacity-0 group-hover:opacity-100 transition-all">
                        <Trash2 size={11} />
                      </button>
                    </div>
                  ))}
                </div>
                <div className="border-t border-gray-700 p-1.5">
                  <button onClick={handleNewDashboard} className="w-full flex items-center gap-2 px-2 py-1.5 rounded-lg text-xs text-gray-400 hover:text-amber-400 hover:bg-amber-900/20 transition-colors">
                    <span className="text-lg leading-none">+</span> Nuevo dashboard
                  </button>
                </div>
              </div>
            )}
          </div>

          {database && (
            <span className="text-xs font-mono text-gray-500 bg-ink-800 border border-gray-700 px-2 py-0.5 rounded-full">
              {database.nombre ?? database}
            </span>
          )}
          {activeDash && (
            <span className="text-xs font-mono text-gray-600">
              {paneles.length} panel{paneles.length !== 1 ? 'es' : ''}
              {dashboards.length > 1 && ` · ${totalPaneles} total`}
            </span>
          )}
        </div>

        <div className="flex items-center gap-2">
          <button onClick={handleNewDashboard} className="flex items-center gap-1.5 text-xs px-3 py-1.5 rounded-lg border border-gray-700 bg-ink-800 text-gray-400 hover:text-amber-400 hover:border-amber-500/40 transition-colors font-mono">
            <span className="text-base leading-none">+</span> Nuevo dashboard
          </button>
          <button onClick={() => setRefreshKey(k => k + 1)} className="flex items-center gap-1.5 text-xs px-3 py-1.5 rounded-lg border border-gray-700 bg-ink-800 text-gray-400 hover:text-azure-400 hover:border-azure-500/40 transition-colors font-mono">
            <RefreshCw size={12} /> Refrescar todo
          </button>
        </div>
      </div>

      {/* Barra de filtros del dashboard */}
      {database && (
        <div className="mb-5 bg-ink-800/60 border border-gray-700/60 rounded-xl px-4 py-2.5 flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-1.5 text-xs text-gray-500 font-medium flex-shrink-0">
            <Filter size={12} className={activeFilterCount > 0 ? 'text-amber-400' : 'text-gray-600'} />
            Filtros del dashboard
            {activeFilterCount > 0 && (
              <span className="ml-1 px-1.5 py-0.5 bg-amber-500/20 text-amber-400 rounded-full text-[10px] font-bold border border-amber-500/30">
                {activeFilterCount} activo{activeFilterCount !== 1 ? 's' : ''}
              </span>
            )}
          </div>
          <div className="flex-1">
            <ContextVarsBar
              database={database}
              contextVars={pendingFilters}
              onUpdate={handleDashFilterChange}
              onMeta={() => {}}
              onApply={handleDashFilterApply}
              onClearAll={handleDashFilterClear}
            />
          </div>
        </div>
      )}

      {/* No database */}
      {!database && (
        <div className="flex flex-col items-center justify-center h-64 gap-4 text-center">
          <Database size={32} className="text-gray-700" />
          <p className="text-sm text-gray-500">Selecciona una base de datos en el header para ver el dashboard.</p>
        </div>
      )}

      {/* Sin dashboards */}
      {database && dashboards.length === 0 && (
        <div className="flex flex-col items-center justify-center h-64 gap-4 text-center">
          <Star size={32} className="text-gray-700" />
          <div>
            <p className="text-sm text-gray-400 font-semibold mb-1">Sin dashboards creados</p>
            <p className="text-xs text-gray-600 max-w-xs">Usa el botón <span className="text-amber-400">★ Favorito</span> en una respuesta para crear tu primer dashboard.</p>
          </div>
          <button onClick={onGoConsulta} className="flex items-center gap-2 text-xs px-3 py-2 rounded-lg bg-azure-600/20 border border-azure-500/30 text-azure-400 hover:bg-azure-600/30 transition-colors">
            <MessageSquare size={13} /> Ir a Consultas
          </button>
        </div>
      )}

      {/* Dashboard activo sin paneles */}
      {database && activeDash && paneles.length === 0 && (
        <div className="flex flex-col items-center justify-center h-64 gap-4 text-center">
          <LayoutDashboard size={32} className="text-gray-700" />
          <div>
            <p className="text-sm text-gray-400 font-semibold mb-1">"{activeDash.nombre}" está vacío</p>
            <p className="text-xs text-gray-600 max-w-xs">Ve a Consultas y usa <span className="text-amber-400">★ Favorito</span> para agregar paneles a este dashboard.</p>
          </div>
          <button onClick={onGoConsulta} className="flex items-center gap-2 text-xs px-3 py-2 rounded-lg bg-azure-600/20 border border-azure-500/30 text-azure-400 hover:bg-azure-600/30 transition-colors">
            <MessageSquare size={13} /> Ir a Consultas
          </button>
        </div>
      )}

      {/* Grid de paneles */}
      {database && paneles.length > 0 && (
        <div key={`${activeDashId}-${refreshKey}`} className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {paneles.map((fav, idx) => (
            <DashboardPanel
              key={`${fav.id}-${refreshKey}`}
              fav={fav}
              database={database}
              appliedFilters={appliedFilters}
              onToggleGlobal={() => handleToggleGlobal(fav.id)}
              onRemove={() => handleRemovePanel(fav.id)}
              onDragStart={() => setDragIdx(idx)}
              onDragOver={() => setDragOverIdx(idx)}
              onDrop={() => handleDrop(idx)}
              isDragOver={dragOverIdx === idx && dragIdx !== idx}
            />
          ))}
        </div>
      )}
    </div>
  )
}

// ─── Filter Config ────────────────────────────────────────────────────────────
// inputType: 'select' (default, dropdown multi-selección) | 'text' (LIKE sobre descripción)
const FILTER_CONFIG = [
  { tipo: 'empresa',       label: 'Empresa',        inputType: 'select', dotBg: 'bg-azure-400',   chipBg: 'bg-azure-900/50',   chipBorder: 'border-azure-500/70',   chipText: 'text-azure-200',   inactiveBorder: 'border-azure-700/50',   inactiveText: 'text-azure-400/60'   },
  { tipo: 'macroproyecto', label: 'Macropro',       inputType: 'select', dotBg: 'bg-purple-400',  chipBg: 'bg-purple-900/50',  chipBorder: 'border-purple-500/70',  chipText: 'text-purple-200',  inactiveBorder: 'border-purple-700/50',  inactiveText: 'text-purple-400/60'  },
  { tipo: 'proyecto',      label: 'Proyecto',       inputType: 'select', dotBg: 'bg-emerald-400', chipBg: 'bg-emerald-900/50', chipBorder: 'border-emerald-500/70', chipText: 'text-emerald-200', inactiveBorder: 'border-emerald-700/50', inactiveText: 'text-emerald-400/60' },
  { tipo: 'estado',        label: 'Estado',         inputType: 'select', dotBg: 'bg-amber-400',   chipBg: 'bg-amber-900/50',   chipBorder: 'border-amber-500/70',   chipText: 'text-amber-200',   inactiveBorder: 'border-amber-700/50',   inactiveText: 'text-amber-400/60'   },
  { tipo: 'insumo',        label: 'Insumo',         inputType: 'text',   dotBg: 'bg-cyan-400',    chipBg: 'bg-cyan-900/50',    chipBorder: 'border-cyan-500/70',    chipText: 'text-cyan-200',    inactiveBorder: 'border-cyan-700/50',    inactiveText: 'text-cyan-400/60',
    // Metadata SQL fija (no hay endpoint para cargar opciones; es texto libre)
    meta: { tabla: 'ADP_DTM_DIM.Insumo', columna: 'Insumo Descripcion' },
    placeholder: 'Buscar descripción (contains)...' },
]

// Hijos jerárquicos: al cambiar un padre se limpian sus hijos
const FILTER_CHILDREN = {
  empresa:       ['macroproyecto', 'proyecto'],
  macroproyecto: ['proyecto'],
}

// ─── Filter Dropdown ──────────────────────────────────────────────────────────
function FilterDropdown({ tipo, label, database, selected, onChange, onMeta, onLabels, onApply, onClose, parentContext }) {
  const [search, setSearch] = useState('')
  const [values, setValues] = useState([])
  const [empresaPorValor, setEmpresaPorValor] = useState({})
  const [metadataPorValor, setMetadataPorValor] = useState({})
  const [descripcionPorValor, setDescripcionPorValor] = useState({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [tooltipInfo, setTooltipInfo] = useState(null) // { meta, x, y }
  const searchRef = useRef(null)

  useEffect(() => {
    const load = async () => {
      try {
        const dbName = typeof database === 'object' ? database.nombre : database
        const res = await datamartApi.getFilterValues(dbName, tipo)
        setValues(res.valores || [])
        if (res.empresaPorValor)     setEmpresaPorValor(res.empresaPorValor)
        if (res.metadataPorValor)    setMetadataPorValor(res.metadataPorValor)
        if (res.descripcionPorValor) { setDescripcionPorValor(res.descripcionPorValor); onLabels?.(res.descripcionPorValor) }
        if (res.tabla && res.columna) onMeta?.({ tabla: res.tabla, columna: res.columna })
      } catch {
        setError('No se pudieron cargar los valores')
      } finally {
        setLoading(false)
      }
    }
    load()
    setTimeout(() => searchRef.current?.focus(), 50)
  }, [database, tipo])

  // Filtrado jerárquico: reduce opciones según selección de padres
  const hierarchyValues = useMemo(() => {
    if (tipo === 'macroproyecto') {
      const empresasSel = parentContext?.empresa || []
      if (empresasSel.length === 0) return values
      return values.filter(v => {
        const emps = empresaPorValor[v] || []
        return emps.some(e => empresasSel.includes(e))
      })
    }
    if (tipo === 'proyecto') {
      const empresasSel = parentContext?.empresa || []
      const macrosSel   = parentContext?.macroproyecto || []
      if (empresasSel.length === 0 && macrosSel.length === 0) return values
      return values.filter(v => {
        const meta = metadataPorValor[v]
        if (!meta) return true
        const okEmpresa = empresasSel.length === 0 || empresasSel.includes(meta.empresa)
        const okMacro   = macrosSel.length   === 0 || macrosSel.includes(meta.macroproyecto)
        return okEmpresa && okMacro
      })
    }
    return values
  }, [tipo, values, parentContext, empresaPorValor, metadataPorValor])

  const filtered = hierarchyValues.filter(v => v.toLowerCase().includes(search.toLowerCase()))
  const allSelected = filtered.length > 0 && filtered.every(v => selected.includes(v))

  const toggle = (v) => onChange(selected.includes(v) ? selected.filter(s => s !== v) : [...selected, v])
  const toggleAll = () => allSelected
    ? onChange(selected.filter(s => !filtered.includes(s)))
    : onChange([...new Set([...selected, ...filtered])])

  return (
    <div className="absolute top-full mt-1 left-0 w-72 bg-ink-800 border border-gray-700 rounded-xl shadow-2xl z-50 overflow-hidden">
      {/* Search */}
      <div className="p-2 border-b border-gray-700">
        <div className="flex items-center gap-2 bg-ink-900 border border-gray-700 focus-within:border-azure-500/50 rounded-lg px-2.5 py-1.5 transition-colors">
          <Search size={12} className="text-gray-500 flex-shrink-0" />
          <input
            ref={searchRef}
            type="text"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder={`Buscar ${label.toLowerCase()}...`}
            className="flex-1 bg-transparent text-xs text-gray-300 placeholder-gray-600 outline-none"
          />
          {search && <button onClick={() => setSearch('')} className="text-gray-600 hover:text-gray-300 text-xs leading-none">✕</button>}
        </div>
      </div>

      {/* Select all / Clear */}
      <div className="flex items-center justify-between px-3 py-1.5 border-b border-gray-800">
        <button onClick={toggleAll} className="text-[11px] text-azure-400 hover:text-azure-300 transition-colors">
          {allSelected ? 'Deseleccionar todo' : 'Seleccionar todo'}
        </button>
        {selected.length > 0 && (
          <button onClick={() => onChange([])} className="text-[11px] text-red-400 hover:text-red-300 transition-colors">
            Limpiar ({selected.length})
          </button>
        )}
      </div>

      {/* List */}
      <div className="max-h-52 overflow-y-auto">
        {loading && (
          <div className="flex items-center gap-2 p-4">
            <Loader2 size={12} className="animate-spin text-azure-400" />
            <span className="text-xs text-gray-500">Cargando valores...</span>
          </div>
        )}
        {error && <p className="text-xs text-red-400 p-3">{error}</p>}
        {!loading && !error && filtered.length === 0 && (
          <p className="text-xs text-gray-600 p-3 text-center">Sin resultados{search ? ` para "${search}"` : ''}</p>
        )}
        {!loading && !error && filtered.map(v => {
          const empresas = empresaPorValor[v]
          const meta     = metadataPorValor[v]
          const desc     = descripcionPorValor[v]

          // Label principal: descripción (macropro) o nombre (proyecto/empresa/estado)
          const displayLabel = desc ?? v
          // Subtext bajo el label
          const subtext = empresas?.length > 0
            ? v  // macroproyecto: subtext = código
            : meta?.codigo ?? null  // proyecto: subtext = código
          // Tooltip nativo solo para macroproyecto sin custom tooltip
          const nativeTooltip = empresas?.length > 0 && !meta
            ? empresas.join(' · ')
            : undefined
          const hasCustomTooltip = !!meta && !!(meta.empresa || meta.macroproyecto)
          return (
            <label
              key={v}
              title={nativeTooltip}
              className="flex items-center gap-2.5 px-3 py-2 hover:bg-ink-700 cursor-pointer transition-colors"
              onMouseEnter={hasCustomTooltip ? (e) => {
                const r = e.currentTarget.getBoundingClientRect()
                setTooltipInfo({ meta, x: r.right + 8, y: r.top + r.height / 2 })
              } : undefined}
              onMouseLeave={hasCustomTooltip ? () => setTooltipInfo(null) : undefined}
            >
              <input
                type="checkbox"
                checked={selected.includes(v)}
                onChange={() => toggle(v)}
                className="w-3.5 h-3.5 rounded accent-blue-500 flex-shrink-0 cursor-pointer"
              />
              <div className="flex-1 min-w-0">
                <span className="text-xs text-gray-300 truncate block">{displayLabel}</span>
                {subtext && subtext !== displayLabel && (
                  <span className="text-[10px] text-gray-600 truncate block leading-tight">{subtext}</span>
                )}
              </div>
            </label>
          )
        })}
      </div>

      {/* Footer */}
      <div className="border-t border-gray-700 px-3 py-2 flex items-center justify-between">
        <span className="text-[11px] text-gray-600 font-mono">{selected.length} seleccionado{selected.length !== 1 ? 's' : ''}</span>
        <button
          onClick={() => { onClose(); onApply?.() }}
          className="text-xs px-3 py-1 bg-azure-600/20 border border-azure-500/30 text-azure-400 rounded-lg hover:bg-azure-600/30 transition-colors"
        >
          Aplicar
        </button>
      </div>

      {/* Tooltip custom fixed para proyecto (escapa overflow-hidden) */}
      {tooltipInfo && (
        <div
          className="pointer-events-none fixed z-[200] -translate-y-1/2"
          style={{ left: tooltipInfo.x, top: tooltipInfo.y }}
        >
          <div className="bg-ink-900 border border-gray-600 rounded-lg shadow-2xl px-3 py-2 w-52">
            {tooltipInfo.meta.empresa && (
              <div className="flex items-center gap-2 mb-1">
                <span className="w-1.5 h-1.5 rounded-full bg-azure-400 flex-shrink-0" />
                <span className="text-[10px] text-gray-500 w-12 flex-shrink-0">Empresa</span>
                <span className="text-[10px] text-azure-300 font-medium truncate">{tooltipInfo.meta.empresa}</span>
              </div>
            )}
            {tooltipInfo.meta.macroproyecto && (
              <div className="flex items-center gap-2">
                <span className="w-1.5 h-1.5 rounded-full bg-purple-400 flex-shrink-0" />
                <span className="text-[10px] text-gray-500 w-12 flex-shrink-0">Macro</span>
                <span className="text-[10px] text-purple-300 font-medium truncate">{tooltipInfo.meta.macroproyecto}</span>
              </div>
            )}
          </div>
          <div className="absolute right-full top-1/2 -translate-y-1/2 border-[5px] border-transparent border-r-gray-600" />
        </div>
      )}
    </div>
  )
}

// ─── Filter Text Input (popup compacto para búsqueda por texto LIKE) ──────────
function FilterTextInput({ tipo, label, placeholder, selected, onChange, onApply, onClose }) {
  const initial = (selected && selected[0]) || ''
  const [text, setText] = useState(initial)
  const inputRef = useRef(null)

  useEffect(() => { setTimeout(() => inputRef.current?.focus(), 50) }, [])

  const applyAndClose = () => {
    const trimmed = text.trim()
    onChange(trimmed ? [trimmed] : [])
    onClose()
    onApply?.()
  }

  const clearAndClose = () => {
    onChange([])
    onClose()
    onApply?.()
  }

  return (
    <div className="absolute top-full mt-1 left-0 w-72 bg-ink-800 border border-gray-700 rounded-xl shadow-2xl z-50 overflow-hidden">
      <div className="p-2 border-b border-gray-700">
        <div className="flex items-center gap-2 bg-ink-900 border border-gray-700 focus-within:border-azure-500/50 rounded-lg px-2.5 py-1.5 transition-colors">
          <Search size={12} className="text-gray-500 flex-shrink-0" />
          <input
            ref={inputRef}
            type="text"
            value={text}
            onChange={e => setText(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') { e.preventDefault(); applyAndClose() }
              if (e.key === 'Escape') { e.preventDefault(); onClose() }
            }}
            placeholder={placeholder || `Buscar ${label.toLowerCase()}...`}
            className="flex-1 bg-transparent text-xs text-gray-300 placeholder-gray-600 outline-none"
          />
          {text && <button onClick={() => setText('')} className="text-gray-600 hover:text-gray-300 text-xs leading-none">✕</button>}
        </div>
        <p className="text-[10px] text-gray-600 mt-1.5 leading-relaxed">
          Se aplica como <span className="font-mono text-gray-500">LIKE '%texto%'</span> sobre la descripción del insumo.
          Si la consulta no involucra la dimensión Insumo, se ignora automáticamente.
        </p>
      </div>

      <div className="border-t border-gray-700 px-3 py-2 flex items-center justify-between">
        <button
          onClick={clearAndClose}
          disabled={!initial && !text}
          className="text-[11px] text-red-400 hover:text-red-300 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
        >
          Limpiar
        </button>
        <button
          onClick={applyAndClose}
          className="text-xs px-3 py-1 bg-azure-600/20 border border-azure-500/30 text-azure-400 rounded-lg hover:bg-azure-600/30 transition-colors"
        >
          Aplicar
        </button>
      </div>
    </div>
  )
}

// ─── Context Vars Bar ─────────────────────────────────────────────────────────
function ContextVarsBar({ database, contextVars, onUpdate, onMeta, onApply, onClearAll }) {
  const [openDropdown, setOpenDropdown] = useState(null)
  const [labelMap, setLabelMap] = useState({}) // { macroproyecto: { code: desc } }
  const wrapperRefs = useRef({})

  const handleLabels = (tipo, descMap) => {
    setLabelMap(prev => ({ ...prev, [tipo]: descMap }))
  }

  // Cierra el dropdown si se hace click fuera
  useEffect(() => {
    if (!openDropdown) return
    const handleClick = (e) => {
      const ref = wrapperRefs.current[openDropdown]
      if (ref && !ref.contains(e.target)) setOpenDropdown(null)
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [openDropdown])

  // Auto-registrar meta de filtros de tipo 'text' (tienen meta hardcodeada en FILTER_CONFIG)
  useEffect(() => {
    FILTER_CONFIG.forEach(f => {
      if (f.inputType === 'text' && f.meta) onMeta(f.tipo, f.meta)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const hasAny = FILTER_CONFIG.some(f => (contextVars[f.tipo] || []).length > 0)

  return (
    <div className="flex items-center gap-1.5 flex-wrap mb-2 min-h-[26px]">
      <SlidersHorizontal size={11} className="text-gray-600 flex-shrink-0" />
      {FILTER_CONFIG.map(({ tipo, label, inputType, placeholder, dotBg, chipBg, chipBorder, chipText, inactiveBorder, inactiveText }) => {
        const selected = contextVars[tipo] || []
        const isActive = selected.length > 0
        const isOpen = openDropdown === tipo
        const isText = inputType === 'text'

        // Texto a mostrar en el chip
        const chipValue = isText
          ? (selected[0] || '')
          : (labelMap[tipo]?.[selected[0]] ?? selected[0])

        return (
          <div
            key={tipo}
            ref={el => wrapperRefs.current[tipo] = el}
            className="relative"
          >
            {isActive ? (
              <div className={`flex items-center gap-1 ${chipBg} border ${chipBorder} rounded-full px-2 py-0.5`}>
                <span className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${dotBg}`} />
                <button
                  onClick={() => setOpenDropdown(isOpen ? null : tipo)}
                  className={`flex items-center gap-1 ${chipText} hover:opacity-80 transition-opacity`}
                >
                  <span className="text-[10px] font-semibold opacity-70 mr-0.5">{label}</span>
                  <span className="text-[11px] max-w-[140px] truncate">
                    {isText ? `"${chipValue}"` : chipValue}
                  </span>
                  {!isText && selected.length > 1 && (
                    <span className="text-[9px] bg-white/20 text-white rounded-full px-1 font-bold">+{selected.length - 1}</span>
                  )}
                </button>
                <button onClick={() => onUpdate(tipo, [])} className="text-gray-600 hover:text-red-400 transition-colors ml-0.5 flex-shrink-0">
                  <X size={9} />
                </button>
              </div>
            ) : (
              <button
                onClick={() => setOpenDropdown(isOpen ? null : tipo)}
                className={`flex items-center gap-1 text-[10px] ${inactiveText} hover:opacity-100 border ${inactiveBorder} rounded-full px-2 py-0.5 transition-opacity`}
              >
                <span className={`w-1 h-1 rounded-full ${dotBg} opacity-70`} />
                {label}
              </button>
            )}

            {isOpen && (
              isText ? (
                <FilterTextInput
                  tipo={tipo}
                  label={label}
                  placeholder={placeholder}
                  selected={selected}
                  onChange={(vals) => onUpdate(tipo, vals)}
                  onApply={onApply}
                  onClose={() => setOpenDropdown(null)}
                />
              ) : (
                <FilterDropdown
                  tipo={tipo}
                  label={label}
                  database={database}
                  selected={selected}
                  onChange={(vals) => onUpdate(tipo, vals)}
                  onMeta={(meta) => onMeta(tipo, meta)}
                  onLabels={(descMap) => handleLabels(tipo, descMap)}
                  onApply={onApply}
                  onClose={() => setOpenDropdown(null)}
                  parentContext={contextVars}
                />
              )
            )}
          </div>
        )
      })}

      {hasAny && (
        <button
          onClick={onClearAll}
          className="text-[10px] text-gray-700 hover:text-red-400 transition-colors ml-1 font-mono"
          title="Limpiar todos los filtros de contexto"
        >
          limpiar todo
        </button>
      )}
    </div>
  )
}

// ─── Schema Panel ─────────────────────────────────────────────────────────────
function SchemaPanel({ schema, vistas, onClose }) {
  const [tab, setTab] = useState('hechos')
  const [schemaSearch, setSchemaSearch] = useState('')
  if (!schema) return null

  const q = schemaSearch.trim().toLowerCase()

  const allHechos = schema.columnas?.filter(c => c.tipoObjeto?.toUpperCase() === 'HECHO') || []
  const allDims = schema.columnas?.filter(c => c.tipoObjeto?.toUpperCase() === 'DIMENSION') || []

  const matchCol = c => !q ||
    c.nombreTabla?.toLowerCase().includes(q) ||
    c.nombreCampo?.toLowerCase().includes(q) ||
    c.descripcionCampo?.toLowerCase().includes(q)

  const hechos = allHechos.filter(matchCol)
  const dims = allDims.filter(matchCol)
  const vistasFiltradas = (vistas || []).filter(v =>
    !q || v.nombreVista?.toLowerCase().includes(q) || v.esquema?.toLowerCase().includes(q)
  )

  const groupBy = (arr, key) => arr.reduce((acc, item) => {
    const k = item[key]; (acc[k] = acc[k] || []).push(item); return acc
  }, {})

  const hechosPorTabla = groupBy(hechos, 'nombreTabla')
  const dimsPorTabla = groupBy(dims, 'nombreTabla')

  return (
    <div className="w-72 bg-ink-900 border-r border-gray-800 flex flex-col overflow-hidden">
      <div className="px-4 py-3 border-b border-gray-800 flex items-center justify-between">
        <span className="text-xs font-mono text-gray-400 uppercase tracking-wider">Schema</span>
        <button onClick={onClose} className="text-gray-600 hover:text-gray-300 text-xs">✕</button>
      </div>
      <div className="px-3 py-2 border-b border-gray-800">
        <div className="flex items-center gap-2 bg-ink-800 border border-gray-700 focus-within:border-azure-500/50 rounded-lg px-2.5 py-1.5 transition-colors">
          <Search size={12} className="text-gray-500 flex-shrink-0" />
          <input
            type="text"
            value={schemaSearch}
            onChange={e => setSchemaSearch(e.target.value)}
            placeholder="Buscar tabla o campo..."
            className="flex-1 bg-transparent text-xs text-gray-300 placeholder-gray-600 outline-none font-mono"
          />
          {schemaSearch && (
            <button onClick={() => setSchemaSearch('')} className="text-gray-600 hover:text-gray-300 text-xs leading-none">✕</button>
          )}
        </div>
      </div>
      <div className="flex border-b border-gray-800">
        {[['hechos', 'Hechos'], ['dims', 'Dims'], ['vistas', 'Vistas']].map(([k, label]) => (
          <button key={k} onClick={() => setTab(k)}
            className={`flex-1 py-2 text-xs font-mono transition-colors ${tab === k ? 'text-azure-400 border-b-2 border-azure-500' : 'text-gray-500 hover:text-gray-300'}`}>
            {label}
          </button>
        ))}
      </div>
      <div className="flex-1 overflow-y-auto p-3 space-y-3">
        {tab === 'hechos' && Object.entries(hechosPorTabla).map(([tabla, cols]) => (
          <div key={tabla} className="bg-ink-800 rounded-lg p-3">
            <p className="text-xs font-mono text-azure-400 font-semibold mb-2 truncate" title={tabla}>{tabla}</p>
            {cols.map(col => (
              <div key={col.nombreCampo} className="flex items-start gap-2 py-0.5">
                {col.esLlave && <span className="text-amber-400 text-xs mt-0.5">⚿</span>}
                <div className="min-w-0">
                  <span className="text-xs text-gray-300 font-mono block truncate">{col.nombreCampo}</span>
                  <span className="text-xs text-gray-600">{col.tipoCampo}</span>
                </div>
              </div>
            ))}
          </div>
        ))}
        {tab === 'dims' && Object.entries(dimsPorTabla).map(([tabla, cols]) => (
          <div key={tabla} className="bg-ink-800 rounded-lg p-3">
            <p className="text-xs font-mono text-emerald-400 font-semibold mb-2 truncate" title={tabla}>{tabla}</p>
            {cols.map(col => (
              <div key={col.nombreCampo} className="flex items-start gap-2 py-0.5">
                {col.esLlave && <span className="text-amber-400 text-xs mt-0.5">⚿</span>}
                <div className="min-w-0">
                  <span className="text-xs text-gray-300 font-mono block truncate">{col.nombreCampo}</span>
                  <span className="text-xs text-gray-600">{col.tipoCampo}</span>
                </div>
              </div>
            ))}
          </div>
        ))}
        {tab === 'vistas' && vistasFiltradas.map(v => (
          <div key={`${v.esquema}.${v.nombreVista}`} className="bg-ink-800 rounded-lg px-3 py-2">
            <p className="text-xs font-mono text-gray-400 truncate">{v.esquema}</p>
            <p className="text-xs font-mono text-gray-200 truncate" title={v.nombreVista}>{v.nombreVista}</p>
          </div>
        ))}
      </div>
    </div>
  )
}

// ─── Main App ─────────────────────────────────────────────────────────────────
export default function App() {
  const { theme, cycleTheme } = useTheme()
  const [connected, setConnected] = useState(null)
  const [databases, setDatabases] = useState([])
  const [dbsLoaded, setDbsLoaded] = useState(false)
  const [ultimaCarga, setUltimaCarga] = useState(null)
  const [selectedDb, setSelectedDb] = useState('')
  const [schema, setSchema] = useState(null)
  const [vistas, setVistas] = useState([])
  const [loadingDb, setLoadingDb] = useState(false)
  const [refreshingDb, setRefreshingDb] = useState(false)
  const [loadingSchema, setLoadingSchema] = useState(false)
  const [messages, setMessages] = useState([])
  const messagesRef = useRef([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [showSchema, setShowSchema] = useState(false)
  const [dbOpen, setDbOpen] = useState(false)
  const [devMode, setDevMode] = useState(false)
  const [showSuggestions, setShowSuggestions] = useState(false)
  const [suggestionTab, setSuggestionTab] = useState('adpro')
  const [suggestionSearch, setSuggestionSearch] = useState('')
  const [suggestionCategory, setSuggestionCategory] = useState('todas')
  const [suggestionSort, setSuggestionSort] = useState('default') // 'default' | 'uso' | 'az'
  const [suggestionUsage, setSuggestionUsage] = useState(() => {
    try { return JSON.parse(localStorage.getItem('suggestion_usage') || '{}') } catch { return {} }
  })
  const [dbSearch, setDbSearch] = useState('')
  const [selectedModel, setSelectedModel] = useState('claude-haiku-4-5-20251001')
  const [modelOpen, setModelOpen] = useState(false)
  const [savedQueries, setSavedQueries] = useState(() => loadSavedQueries())
  const [showHistory, setShowHistory] = useState(false)
  const [sqlMode, setSqlMode] = useState(false)
  const [sessionUsage, setSessionUsage] = useState({ costoUsd: 0, tokensIn: 0, tokensOut: 0, requests: 0 })
  const [vistaActiva, setVistaActiva] = useState('consulta')
  const [dashboards, setDashboards] = useState(() => loadDashboards())
  const [favModal, setFavModal] = useState(null) // { titulo, sql, tipoRespuesta, grafico }
  const [contextVars, setContextVars] = useState({ empresa: [], proyecto: [], macroproyecto: [], estado: [], insumo: [] })
  const [filterMeta, setFilterMeta] = useState({})
  const filterMetaRef = useRef({})
  const messagesEndRef = useRef(null)
  const dbSearchRef = useRef(null)
  const inputRef = useRef(null)
  const selectedDbRef = useRef(selectedDb)
  const sendingRef = useRef(sending)
  const savedQueriesRef = useRef(savedQueries)
  const schemaRef = useRef(schema)
  const vistasRef = useRef(vistas)
  const selectedModelRef = useRef(selectedModel)
  const sqlModeRef = useRef(sqlMode)
  const contextVarsRef = useRef(contextVars)
  useEffect(() => { selectedDbRef.current = selectedDb }, [selectedDb])
  useEffect(() => { sendingRef.current = sending }, [sending])
  useEffect(() => { savedQueriesRef.current = savedQueries }, [savedQueries])
  useEffect(() => { schemaRef.current = schema }, [schema])
  useEffect(() => { vistasRef.current = vistas }, [vistas])
  useEffect(() => { selectedModelRef.current = selectedModel }, [selectedModel])
  useEffect(() => { sqlModeRef.current = sqlMode }, [sqlMode])
  useEffect(() => { contextVarsRef.current = contextVars }, [contextVars])
  useEffect(() => { filterMetaRef.current = filterMeta }, [filterMeta])
  useEffect(() => { messagesRef.current = messages }, [messages])

  // Initial ping only — databases se cargan bajo demanda
  useEffect(() => {
    const init = async () => {
      try {
        const ping = await datamartApi.ping()
        setConnected(ping.connected)
      } catch {
        setConnected(false)
      }
    }
    init()
  }, [])

  useEffect(() => {
    const handler = (e) => { if (e.key === 'Escape') setShowSuggestions(false) }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [])


  const cargarBases = async () => {
    setLoadingDb(true)
    try {
      const res = await datamartApi.getDatabases()
      setDatabases(res.databases ?? res)
      setUltimaCarga(res.ultimaCarga ?? null)
      setDbsLoaded(true)
    } catch {
      setDatabases([])
    } finally {
      setLoadingDb(false)
    }
  }

  const refrescarBases = async () => {
    setRefreshingDb(true)
    try {
      const res = await datamartApi.refreshDatabases()
      setDatabases(res.databases ?? res)
      setUltimaCarga(res.ultimaCarga ?? null)
      setDbsLoaded(true)
    } catch {
      // silencioso, mantiene el cache anterior
    } finally {
      setRefreshingDb(false)
    }
  }

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const handleSelectDb = async (db) => {
    setSelectedDb(db)
    setDbOpen(false)
    setMessages([])
    setSchema(null)
    setContextVars({ empresa: [], proyecto: [], macroproyecto: [], estado: [], insumo: [] })
    setVistaActiva(dashboards.length > 0 ? 'dashboard' : 'sugeridas')
    setLoadingSchema(true)
    try {
      const ctx = await datamartApi.getSchema(db.nombre)
      setSchema(ctx)
      setVistas(ctx.vistas || [])

      const hechos = ctx.columnas?.filter(c => c.tipoObjeto?.toUpperCase() === 'HECHO').length || 0
      const dims = ctx.columnas?.filter(c => c.tipoObjeto?.toUpperCase() === 'DIMENSION').length || 0
      const tablas = new Set(ctx.columnas?.map(c => c.nombreTabla)).size

      setMessages([{
        role: 'assistant',
        content: null,
        response: {
          tipoRespuesta: 'Texto',
          explicacionTexto: `Base de datos **${db.nombre}** cargada exitosamente. Encontré ${tablas} tablas con ${hechos} campos de hechos y ${dims} dimensiones. ¿Qué deseas analizar?`,
          sqlGenerado: null,
          datos: null,
          grafico: null,
          mensajeError: null
        }
      }])
    } catch (e) {
      const is404 = e?.response?.status === 404
      const serverMsg = e?.response?.data?.error || e.message
      setSelectedDb('')
      setMessages([{
        role: 'assistant',
        content: null,
        response: {
          tipoRespuesta: 'Error',
          explicacionTexto: is404
            ? `La base de datos **${db.nombre}** no está disponible en el servidor YOSEMITE.`
            : `Error cargando la base de datos.`,
          sqlGenerado: null,
          datos: null,
          grafico: null,
          mensajeError: is404
            ? `${serverMsg} Usa el botón de refresco (↻) en el selector de bases de datos para actualizar la lista.`
            : serverMsg
        }
      }])
    } finally {
      setLoadingSchema(false)
    }
  }

  const handleSend = useCallback(async (preguntaDirecta = null) => {
    const currentDb = selectedDbRef.current
    const isSending = sendingRef.current
    const pregunta = (preguntaDirecta ?? input).trim()
    if (!pregunta || !currentDb || isSending) return

    if (!preguntaDirecta) setInput('')
    setSending(true)

    setMessages(prev => [
      ...prev,
      { role: 'user', content: pregunta },
      { role: 'thinking' }
    ])

    const isSqlMode = sqlModeRef.current && !preguntaDirecta

    try {
      if (isSqlMode) {
        const result = await datamartApi.executeQuery(currentDb.nombre, pregunta)
        const response = {
          tipoRespuesta: result.exitoso ? 'Tabla' : 'Error',
          explicacionTexto: result.exitoso
            ? `${result.totalFilas} fila(s) · ${Math.round(result.tiempoEjecucionMs)}ms`
            : null,
          sqlGenerado: pregunta,
          datos: result.datos,
          grafico: null,
          mensajeError: result.exitoso ? null : result.error,
          esSqlDirecto: true
        }
        setMessages(prev => {
          const withoutThinking = prev.filter(m => m.role !== 'thinking')
          return [...withoutThinking, { role: 'assistant', content: null, response }]
        })
      } else {
        // Pasar solo los filtros que tienen valores seleccionados
        const activeFilters = Object.fromEntries(
          Object.entries(contextVarsRef.current).filter(([, v]) => v.length > 0)
        )
        const activeMeta = Object.keys(activeFilters).length > 0
          ? Object.fromEntries(
              Object.entries(filterMetaRef.current).filter(([k]) => activeFilters[k])
            )
          : null
        const result = await datamartApi.analyze({
          database: currentDb.nombre,
          pregunta,
          schema: schemaRef.current?.columnas || [],
          vistas: vistasRef.current || [],
          historialContexto: null,
          modelo: selectedModelRef.current,
          contextoVariables: Object.keys(activeFilters).length > 0 ? activeFilters : null,
          filterMeta: activeMeta && Object.keys(activeMeta).length > 0 ? activeMeta : null
        })
        if (result.usage) {
          setSessionUsage(prev => ({
            costoUsd: prev.costoUsd + (result.usage.costoUsd ?? 0),
            tokensIn:  prev.tokensIn  + (result.usage.tokensEntrada ?? 0),
            tokensOut: prev.tokensOut + (result.usage.tokensSalida  ?? 0),
            requests:  prev.requests  + 1
          }))
        }
        setMessages(prev => {
          const withoutThinking = prev.filter(m => m.role !== 'thinking')
          return [...withoutThinking, { role: 'assistant', content: null, response: result }]
        })
      }
    } catch (e) {
      setMessages(prev => {
        const withoutThinking = prev.filter(m => m.role !== 'thinking')
        return [...withoutThinking, {
          role: 'assistant',
          content: null,
          response: {
            tipoRespuesta: 'Error',
            explicacionTexto: 'No se pudo procesar la solicitud.',
            mensajeError: e.message,
            sqlGenerado: null, datos: null, grafico: null
          }
        }]
      })
    } finally {
      setSending(false)
      setTimeout(() => inputRef.current?.focus(), 100)
    }
  }, [input])

  const handleKeyDown = (e) => {
    if (sqlMode) {
      if (e.key === 'Enter' && e.ctrlKey) { e.preventDefault(); handleSend() }
    } else {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend() }
    }
  }

  const handleSaveQuery = useCallback((pregunta, sqlGenerado, tipoRespuesta, grafico) => {
    const entry = {
      id: Date.now(),
      pregunta,
      sql: sqlGenerado,
      tipoRespuesta,
      grafico: grafico || null,
      fecha: new Date().toISOString()
    }
    setSavedQueries(prev => {
      const next = [entry, ...prev]
      persistSavedQueries(next)
      return next
    })
  }, [])

  const handleAddFavorite = useCallback(({ panelTitulo, dashId, nuevoDashNombre, sql, tipoRespuesta, grafico, esPrebuilt, prebuiltKey, filtrosActivos }) => {
    const panel = {
      id: newPanelId(), titulo: panelTitulo,
      sql, sqlBase: sql,
      esPrebuilt: !!esPrebuilt,
      prebuiltKey: prebuiltKey ?? null,
      filtrosFijos: filtrosActivos ?? {},
      aceptaFiltrosGlobales: true,
      tipoRespuesta, grafico: grafico ?? null,
    }
    setDashboards(prev => {
      let next
      if (dashId) {
        // Agregar a dashboard existente
        next = prev.map(d => d.id === dashId ? { ...d, paneles: [...d.paneles, panel] } : d)
      } else {
        // Crear nuevo dashboard con el panel
        const nuevoId = newDashId()
        next = [...prev, { id: nuevoId, nombre: nuevoDashNombre || 'Nuevo Dashboard', paneles: [panel] }]
      }
      persistDashboards(next)
      return next
    })
    setFavModal(null)
  }, [])

  const handleFilterChange = useCallback((tipo, values) => {
    setContextVars(prev => {
      const next = { ...prev, [tipo]: values }
      // Limpiar hijos jerárquicos cuando cambia el padre
      const children = FILTER_CHILDREN[tipo] || []
      children.forEach(child => { next[child] = [] })
      return next
    })
  }, [])

  const handleFilterMeta = useCallback((tipo, meta) => {
    setFilterMeta(prev => ({ ...prev, [tipo]: meta }))
  }, [])

  const handleFilterClearAll = useCallback(() => {
    setContextVars({ empresa: [], proyecto: [], macroproyecto: [], estado: [], insumo: [] })
  }, [])

  const handleFilterApply = useCallback(() => {
    const lastUserMsg = [...messagesRef.current].reverse().find(m => m.role === 'user')
    if (lastUserMsg) handleSend(lastUserMsg.content)
  }, [handleSend])

  const handleDeleteSaved = useCallback((id) => {
    setSavedQueries(prev => {
      const next = prev.filter(q => q.id !== id)
      persistSavedQueries(next)
      return next
    })
  }, [])

  const handleRunSaved = useCallback(async (saved) => {
    const currentDb = selectedDbRef.current
    if (sendingRef.current || !currentDb) return
    setShowHistory(false)
    setSending(true)

    setMessages(prev => [
      ...prev,
      { role: 'user', content: saved.pregunta, fromHistory: true },
      { role: 'thinking' }
    ])

    try {
      const result = await datamartApi.executeQuery(currentDb.nombre, saved.sql)
      const datos = result.datos ?? result.Datos ?? []
      const exitoso = result.exitoso ?? result.Exitoso ?? false
      const error = result.error ?? result.Error ?? null
      setMessages(prev => {
        const withoutThinking = prev.filter(m => m.role !== 'thinking')
        return [...withoutThinking, {
          role: 'assistant',
          content: null,
          fromHistory: true,
          response: {
            tipoRespuesta: saved.tipoRespuesta,
            explicacionTexto: saved.pregunta,
            sqlGenerado: saved.sql,
            datos,
            grafico: saved.grafico || null,
            mensajeError: exitoso ? null : error
          }
        }]
      })
    } catch (e) {
      setMessages(prev => {
        const withoutThinking = prev.filter(m => m.role !== 'thinking')
        return [...withoutThinking, {
          role: 'assistant',
          content: null,
          response: {
            tipoRespuesta: 'Error',
            explicacionTexto: 'Error ejecutando consulta guardada.',
            mensajeError: e.message,
            sqlGenerado: saved.sql, datos: null, grafico: null
          }
        }]
      })
    } finally {
      setSending(false)
      setTimeout(() => inputRef.current?.focus(), 100)
    }
  }, [])

  const handleSuggestion = useCallback((pregunta) => {
    const newUsage = { ...suggestionUsage, [pregunta]: (suggestionUsage[pregunta] || 0) + 1 }
    setSuggestionUsage(newUsage)
    try { localStorage.setItem('suggestion_usage', JSON.stringify(newUsage)) } catch {}
    handleSend(pregunta)
  }, [handleSend, suggestionUsage])

  const suggestionsAdpro = [
    { pregunta: 'Listar proyectos con su respectivo estado', categoria: 'Proyectos' },
    { pregunta: 'Listar los macroproyectos del sistema incluyendo la empresa y sus respectivos codigos', categoria: 'Proyectos' },
    { pregunta: '¿Cuál es el costo ejecutado vs presupuestado por proyecto de construcción?', categoria: 'Costos' },
    { pregunta: '¿Cuáles son los capítulos o ítems de obra con mayor desviación entre presupuesto y ejecución?', categoria: 'Costos' },
    { pregunta: '¿Cuál es el costo acumulado por tipo de recurso (materiales, mano de obra, equipos) por proyecto?', categoria: 'Costos' },
    { pregunta: '¿Cómo ha evolucionado el gasto mensual de obra en cada proyecto este año?', categoria: 'Costos' },
    { pregunta: '¿Cuál es el costo por metro cuadrado construido en cada proyecto activo?', categoria: 'Costos' },
    { pregunta: '¿Qué materiales e insumos tienen mayor consumo en los proyectos activos?', categoria: 'Materiales' },
    { pregunta: '¿Qué proveedores concentran el mayor gasto en materiales de construcción?', categoria: 'Materiales' },
    { pregunta: '¿Cuál es el valor del inventario de mis proyectos en ejecución?', categoria: 'Materiales' },
    { pregunta: 'Participación porcentual de cada tipo de insumo en el costo consumido total', categoria: 'Materiales' },
    { pregunta: '¿Cuáles son los contratos de subcontratistas con mayor valor ejecutado vs contratado?', categoria: 'Contratos' },
    { pregunta: '¿Cuál es el valor de actas cobradas por subcontratista en cada proyecto?', categoria: 'Contratos' },
    { pregunta: 'Pareto de los proyectos según su desviación del asegurado respecto al proyectado (Asegurado mayor que el proyectado), adicionalmente organizados de forma descendente según su ejecución fisica', categoria: 'Análisis' },
    { pregunta: 'Distribución porcentual por tipo de costo: presupuestado, proyectado y consumido', categoria: 'Análisis' },
    { pregunta: 'Distribución porcentual por tipo de insumo: presupuestado, proyectado y consumido', categoria: 'Análisis' },
    { pregunta: 'Proyección de demanda y costo unitario de los principales insumos de compras — tendencia OLS con ajuste estacional', categoria: 'Análisis' },
    { pregunta: '¿Cuál es el índice de desempeño del costo (CPI) de cada proyecto activo?', categoria: 'Análisis' },
    { pregunta: '¿Cuántos proyectos hay por ciudad y estado?', categoria: 'Proyectos' },
    { pregunta: '¿Cuál es el costo ejecutado vs presupuestado agrupado por macroproyecto?', categoria: 'Costos' },
    { pregunta: '¿Cuál es el valor mensual de materiales ingresados al almacén este año?', categoria: 'Materiales' },
    { pregunta: '¿Cuál es el stock estimado de insumos (entradas menos salidas) en proyectos activos?', categoria: 'Materiales' },
    // Familia 1 — Desviación y control del baseline
    { pregunta: 'IRA — ¿Cuánto se ha desviado el proyectado actual respecto al presupuestado original por proyecto?', categoria: 'Control' },
    { pregunta: 'CDT — ¿Qué capítulos concentran la mayor parte de la desviación total del proyecto (análisis Pareto por capítulo)?', categoria: 'Control' },
    { pregunta: 'VAC — ¿Cuánto se estima que costará adicional al presupuesto original y cuál es el costo final estimado (EAC) de cada proyecto?', categoria: 'Control' },
    // Familia 2 — Compromiso contractual y aseguramiento
    { pregunta: '%A — ¿Qué porcentaje del presupuesto proyectado ya está asegurado mediante compras o contratos firmados por proyecto?', categoria: 'Aseguramiento' },
    { pregunta: 'ICC — ¿Cuánto del dinero ya comprometido (asegurado) se ha convertido efectivamente en costo causado (invertido) por proyecto?', categoria: 'Aseguramiento' },
    { pregunta: 'RCC — ¿Qué proporción del aseguramiento está cubierta por órdenes de compra versus contratos de subcontrato por proyecto?', categoria: 'Aseguramiento' },
    { pregunta: '%NA — ¿Cuánto del presupuesto de cada capítulo aún no tiene respaldo contractual ni orden de compra?', categoria: 'Aseguramiento' },
    // Familia 3 — Eficiencia operativa en ejecución
    { pregunta: 'RR/APU — ¿Cuánto valor ganado se obtiene por cada peso consumido en cada capítulo de obra (rendimiento relativo Ejecutado/Consumido)?', categoria: 'Eficiencia' },
    { pregunta: 'IONC — ¿Cuánto del insumo causado contablemente (invertido) aún no ha sido consumido en obra por proyecto?', categoria: 'Eficiencia' },
    { pregunta: 'TQP — ¿A qué ritmo se está quemando el presupuesto mensualmente en cada proyecto activo este año (Tasa de Quemado Presupuestal)?', categoria: 'Eficiencia' },
    { pregunta: 'SPI — ¿Cuánto falta por invertir para completar cada proyecto (saldo por invertir)?', categoria: 'Eficiencia' },
    { pregunta: 'RCI — ¿Qué porcentaje del costo causado (invertido) ha sido efectivamente consumido en obra y qué parte permanece en almacén?', categoria: 'Eficiencia' },
    // Familia 4 — Calidad de precio y gestión de compras
    { pregunta: 'DUC — ¿En cuánto se desvía el valor real de los insumos comprados respecto al presupuestado por ítem?', categoria: 'Compras' },
    { pregunta: 'IRI — ¿Qué ítems presentan simultáneamente mayor desviación de precio y alto peso dentro del presupuesto (índice de riesgo de ítem)?', categoria: 'Compras' },
    { pregunta: 'III — ¿Cómo ha evolucionado el precio unitario de los insumos más comprados entre compras sucesivas (inflación interna de insumo)?', categoria: 'Compras' },
    // Familia 5 — Proyección y aprendizaje
    { pregunta: 'IAFF — ¿El ritmo de causación contable (invertido) está alineado con el avance físico reportado en cada proyecto?', categoria: 'Proyección' },
    { pregunta: 'EPP — ¿Qué tan confiable es el proyectado como predictor del costo real invertido en cada proyecto?', categoria: 'Proyección' },
    { pregunta: 'BIP — Comparativo del desempeño de todas las obras activas del portafolio en los siete conceptos de control (benchmark interno)', categoria: 'Proyección' },
    { pregunta: 'GMC — ¿Cómo han evolucionado mes a mes los siete conceptos de control (presupuestado, proyectado, asegurado, invertido, consumido, ejecutado) en el portafolio activo?', categoria: 'Proyección' },
  ]

  const suggestionsSrm = [
    { pregunta: '¿Cuáles son las licitaciones con mayor valor adjudicado?', categoria: 'Licitaciones' },
    { pregunta: '¿Cuántas licitaciones se han creado por mes y cuál es su valor?', categoria: 'Licitaciones' },
    { pregunta: '¿Cuáles son las licitaciones más recientes con sus proveedores adjudicados?', categoria: 'Licitaciones' },
    { pregunta: '¿Cuál es el valor licitado vs presupuestado por actividad?', categoria: 'Licitaciones' },
    { pregunta: '¿Qué proveedores han recibido más adjudicaciones y por qué valor?', categoria: 'Proveedores' },
    { pregunta: '¿Cuáles son los proveedores adjudicados por proyecto en licitaciones?', categoria: 'Proveedores' },
    { pregunta: '¿Cuál es el resumen de adjudicaciones por estado del proveedor?', categoria: 'Proveedores' },
    { pregunta: '¿De qué ciudades provienen los proveedores con mayor valor adjudicado?', categoria: 'Proveedores' },
    { pregunta: '¿Cuál es el valor total adjudicado en licitaciones por proyecto?', categoria: 'Adjudicaciones' },
    { pregunta: '¿Qué actividades concentran el mayor valor en las licitaciones?', categoria: 'Adjudicaciones' },
    { pregunta: '¿Qué insumos se incluyen con mayor valor en las licitaciones?', categoria: 'Adjudicaciones' },
  ]

  const catColorsAdpro = {
    Proyectos: '#3b82f6', Costos: '#f59e0b', Materiales: '#10b981',
    Contratos: '#f97316', Análisis: '#8b5cf6',
    Control: '#ef4444', Aseguramiento: '#6366f1',
    Eficiencia: '#14b8a6', Compras: '#fb923c', Proyección: '#a855f7',
  }
  const catColorsSrm  = { Licitaciones: '#6366f1', Proveedores: '#06b6d4', Adjudicaciones: '#ec4899' }
  const catColors = suggestionTab === 'adpro' ? catColorsAdpro : catColorsSrm

  const allSuggestions = suggestionTab === 'adpro' ? suggestionsAdpro : suggestionsSrm
  const categories = [...new Set(allSuggestions.map(s => s.categoria))]

  const getSuggestionList = (cat, search, sort) => {
    let list = allSuggestions
    if (cat === 'top') list = list.filter(s => (suggestionUsage[s.pregunta] || 0) > 0)
    else if (cat !== 'todas') list = list.filter(s => s.categoria === cat)
    if (search) list = list.filter(s => s.pregunta.toLowerCase().includes(search.toLowerCase()))
    if (sort === 'uso' || cat === 'top') list = [...list].sort((a, b) => (suggestionUsage[b.pregunta] || 0) - (suggestionUsage[a.pregunta] || 0))
    else if (sort === 'az') list = [...list].sort((a, b) => a.pregunta.localeCompare(b.pregunta))
    return list
  }

  // Compatibilidad con usos anteriores (inline empty-state)
  const suggestions = allSuggestions

  return (
    <div className="h-screen flex flex-col bg-ink-950 grid-bg overflow-hidden">

      {/* Header estilo SincoERP */}
      <header className="flex-shrink-0 border-b border-gray-800/60 px-4" style={{ background: '#1B344C', minHeight: 52 }}>
        <div className="flex items-center justify-between h-[52px] gap-3">
          {/* Logo SINCO + badge ADPRO */}
          <div className="flex items-center gap-3">
            <div className="flex items-center gap-2">
              <SincoLogo size={24} />
              <div className="flex flex-col leading-none">
                <span className="text-white font-bold text-sm tracking-[0.12em]">SINCO<span style={{ color: '#0AABB8' }}>ERP</span></span>
                <span className="text-white/40 text-[9px] tracking-widest uppercase">+ Plus</span>
              </div>
            </div>
            <div className="w-px h-6 bg-white/15" />
            <div className="flex items-center gap-2 px-2.5 py-1 rounded" style={{ background: 'linear-gradient(90deg, #1D7478, #058C97)' }}>
              <BarChart2 size={13} className="text-white" />
              <span className="text-white text-xs font-medium tracking-widest">ADPRO</span>
              <div className="w-px h-5 bg-white/25" />
              <div className="flex flex-col leading-none gap-[2px]">
                <span className="text-white text-[10px] font-semibold tracking-wider">Datamart</span>
                <span className="text-white/55 text-[9px] tracking-wider">Analyzer</span>
              </div>
            </div>
            <div className="w-px h-5 bg-white/10" />
            {/* Nav tabs */}
            <div className="flex items-center gap-1 bg-black/20 rounded-lg p-0.5 border border-white/10">
              <button
                onClick={() => { setVistaActiva('consulta'); setShowSuggestions(false) }}
                disabled={!selectedDb}
                title={!selectedDb ? 'Selecciona una base de datos primero' : ''}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md text-xs font-medium transition-all ${!selectedDb ? 'opacity-35 cursor-not-allowed text-white/30' : vistaActiva === 'consulta' && !showSuggestions ? 'bg-amber-600/20 text-amber-400 border border-amber-500/30' : 'text-white/50 hover:text-white/80'}`}
              >
                <MessageSquare size={11} /> Consulta
              </button>
              <button
                onClick={() => { setVistaActiva('dashboard'); setShowSuggestions(false) }}
                disabled={!selectedDb}
                title={!selectedDb ? 'Selecciona una base de datos primero' : ''}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md text-xs font-medium transition-all relative ${!selectedDb ? 'opacity-35 cursor-not-allowed text-white/30' : vistaActiva === 'dashboard' ? 'bg-amber-600/20 text-amber-400 border border-amber-500/30' : 'text-white/50 hover:text-white/80'}`}
              >
                <LayoutDashboard size={11} /> Dashboard
                {dashboards.length > 0 && (
                  <span className="absolute -top-1 -right-1 min-w-[15px] h-[15px] px-0.5 bg-amber-500 text-white text-[9px] font-bold rounded-full flex items-center justify-center">
                    {dashboards.length}
                  </span>
                )}
              </button>
              <button
                onClick={() => { setVistaActiva(vistaActiva === 'sugeridas' ? 'consulta' : 'sugeridas'); setSuggestionSearch('') }}
                disabled={!selectedDb}
                title={!selectedDb ? 'Selecciona una base de datos primero' : 'Preguntas sugeridas'}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md text-xs font-medium transition-all ${!selectedDb ? 'opacity-35 cursor-not-allowed text-white/30' : vistaActiva === 'sugeridas' ? 'bg-amber-600/20 text-amber-300 border border-amber-500/30' : 'text-white/50 hover:text-white/80'}`}
              >
                <Lightbulb size={11} /> Sugeridas
              </button>
            </div>
          </div>

          <div className="flex items-center gap-3">
            {/* Theme toggle */}
            {(() => {
              const t = THEMES.find(t => t.id === theme)
              return (
                <button
                  onClick={cycleTheme}
                  title={`Tema: ${t.label} — click para cambiar`}
                  className="flex items-center gap-1.5 px-2 py-1 rounded bg-black/20 border border-white/10 hover:border-white/25 transition-colors"
                >
                  <t.Icon size={12} className={t.iconClass} />
                  <span className={`text-[10px] font-mono ${t.iconClass}`}>{t.label}</span>
                </button>
              )
            })()}

            {/* Connection status */}
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-black/20 border border-white/10">
              <span className={`w-1.5 h-1.5 rounded-full ${connected ? 'bg-emerald-400' : 'bg-red-400'}`}
                style={connected ? { boxShadow: '0 0 5px #34d399' } : {}} />
              <Server size={11} className="text-white/50" />
              <span className="text-xs font-mono text-white/60">YOSEMITE</span>
            </div>

            {/* Session cost */}
            {sessionUsage.requests > 0 && (
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-black/20 border border-white/10" title={`${sessionUsage.requests} consultas IA · ${(sessionUsage.tokensIn + sessionUsage.tokensOut).toLocaleString('en-US')} tokens`}>
                <span className="text-[10px] font-mono text-emerald-400/80">~${sessionUsage.costoUsd.toFixed(4)}</span>
                <span className="text-[10px] font-mono text-white/30">USD</span>
              </div>
            )}

            {/* DB Selector */}
            <div className="relative">
              <button onClick={() => { setDbOpen(v => { if (!v) { setTimeout(() => dbSearchRef.current?.focus(), 50); if (!dbsLoaded) cargarBases() } return !v }); setDbSearch('') }}
                className="flex items-center gap-2 bg-black/20 hover:bg-black/30 border border-white/15 hover:border-white/25 rounded-lg px-3 py-1.5 text-xs font-mono transition-colors">
                <Database size={12} className="text-white/60" />
                <span className="text-white/90">{selectedDb ? selectedDb.nombre : 'Seleccionar BD'}</span>
                {loadingDb ? <Loader2 size={12} className="animate-spin text-white/40" /> : <ChevronDown size={12} className="text-white/40" />}
              </button>
              {dbOpen && (
                <div className="absolute right-0 top-full mt-1 w-72 bg-ink-800 border border-gray-700 rounded-xl shadow-xl z-50 overflow-hidden">
                  <div className="p-2 border-b border-gray-700 space-y-1.5">
                    <div className="flex items-center justify-between px-1">
                      <p className="text-xs text-gray-500 font-mono">Servidor: YOSEMITE</p>
                      <button
                        onClick={e => { e.stopPropagation(); refrescarBases() }}
                        disabled={refreshingDb}
                        title="Refrescar lista de bases de datos"
                        className="flex items-center gap-1 text-xs text-gray-500 hover:text-azure-400 disabled:opacity-40 transition-colors font-mono"
                      >
                        <RefreshCw size={11} className={refreshingDb ? 'animate-spin' : ''} />
                        {refreshingDb ? 'Actualizando...' : 'Refrescar'}
                      </button>
                    </div>
                    {ultimaCarga && (
                      <p className="text-xs text-gray-600 font-mono px-1">
                        Actualizado: {new Date(ultimaCarga).toLocaleTimeString()}
                      </p>
                    )}
                    <div className="relative">
                      <input
                        ref={dbSearchRef}
                        type="text"
                        value={dbSearch}
                        onChange={e => setDbSearch(e.target.value)}
                        placeholder="Buscar base de datos..."
                        className="w-full bg-ink-900 border border-gray-600 focus:border-azure-500/60 rounded-lg px-3 py-1.5 text-xs font-mono text-gray-200 placeholder-gray-600 outline-none transition-colors"
                      />
                    </div>
                  </div>
                  <div className="max-h-64 overflow-y-auto">
                    {loadingDb && (
                      <div className="flex items-center gap-2 p-3">
                        <Loader2 size={12} className="animate-spin text-azure-400" />
                        <span className="text-xs text-gray-400 font-mono">Cargando bases de datos...</span>
                      </div>
                    )}
                    {!loadingDb && !dbsLoaded && (
                      <div className="p-3 text-center">
                        <p className="text-xs text-gray-500 font-mono mb-2">Lista no cargada</p>
                        <button onClick={cargarBases} className="text-xs text-azure-400 hover:text-azure-300 font-mono underline">
                          Cargar ahora
                        </button>
                      </div>
                    )}
                    {!loadingDb && dbsLoaded && databases
                      .filter(db => db.nombre.toLowerCase().includes(dbSearch.toLowerCase()))
                      .map(db => (
                        <button key={db.nombre} onClick={() => { setDbSearch(''); handleSelectDb(db) }}
                          className={`w-full flex items-center justify-between px-3 py-2.5 hover:bg-ink-700 text-left transition-colors ${selectedDb?.nombre === db.nombre ? 'bg-azure-600/10' : ''}`}>
                          <span className="text-xs font-mono text-gray-200">{db.nombre}</span>
                          {db.esDatamart && (
                            <span className="text-xs px-1.5 py-0.5 bg-emerald-900/50 text-emerald-400 rounded font-mono">DTM</span>
                          )}
                        </button>
                      ))
                    }
                    {!loadingDb && dbsLoaded && databases.filter(db => db.nombre.toLowerCase().includes(dbSearch.toLowerCase())).length === 0 && dbSearch && (
                      <p className="text-xs text-gray-500 font-mono p-3">Sin resultados para "{dbSearch}"</p>
                    )}
                    {!loadingDb && dbsLoaded && !databases.length && (
                      <p className="text-xs text-gray-500 font-mono p-3">Sin bases de datos disponibles</p>
                    )}
                  </div>
                </div>
              )}
            </div>

            {/* Schema toggle */}
            {schema && (
              <button onClick={() => setShowSchema(v => !v)}
                className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-mono border transition-colors ${showSchema ? 'border-white/30 text-white bg-white/15' : 'bg-black/20 border-white/15 text-white/60 hover:text-white/90 hover:bg-black/30'}`}>
                <Layers size={12} />Schema
              </button>
            )}

            {/* Model selector */}
            <div className="relative">
              <button
                onClick={() => setModelOpen(v => !v)}
                title="Seleccionar modelo de IA"
                className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-mono border bg-black/20 border-white/15 text-white/60 hover:text-white/90 hover:bg-black/30 transition-colors"
              >
                <Zap size={12} />
                {AI_MODELS.find(m => m.id === selectedModel)?.label ?? 'Modelo'}
                <ChevronDown size={11} className="text-white/40" />
              </button>
              {modelOpen && (
                <div className="absolute right-0 top-full mt-1 w-56 bg-ink-800 border border-gray-700 rounded-xl shadow-xl z-50 overflow-hidden">
                  {AI_MODELS.map(m => (
                    <button
                      key={m.id}
                      onClick={() => { setSelectedModel(m.id); setModelOpen(false) }}
                      className={`w-full flex flex-col px-3 py-2.5 hover:bg-ink-700 text-left transition-colors ${selectedModel === m.id ? 'bg-azure-600/10' : ''}`}
                    >
                      <span className={`text-xs font-mono font-semibold ${selectedModel === m.id ? 'text-azure-400' : 'text-gray-200'}`}>{m.label}</span>
                      <span className="text-[10px] text-gray-500 font-mono">{m.desc}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* Dev mode toggle */}
            <button onClick={() => setDevMode(v => !v)}
              title="Modo desarrollador: muestra el SQL generado"
              className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-mono border transition-colors ${devMode ? 'bg-amber-600/20 border-amber-500/40 text-amber-400' : 'bg-black/20 border-white/15 text-white/60 hover:text-white/90 hover:bg-black/30'}`}>
              <Code size={12} />Dev
            </button>
          </div>
        </div>

        {/* Filtros de contexto en el header — solo en consulta con BD seleccionada */}
        {selectedDb && (vistaActiva === 'consulta' || vistaActiva === 'sugeridas') && !sqlMode && (
          <div className="border-t border-white/10 py-1.5">
            <ContextVarsBar
              database={selectedDb}
              contextVars={contextVars}
              onUpdate={handleFilterChange}
              onMeta={handleFilterMeta}
              onApply={handleFilterApply}
              onClearAll={handleFilterClearAll}
            />
          </div>
        )}
      </header>

      {/* Body */}
      <div className="flex-1 flex overflow-hidden relative">

        {/* Dashboard View */}
        {vistaActiva === 'dashboard' && (
          <Dashboard
            dashboards={dashboards}
            setDashboards={setDashboards}
            database={selectedDb}
            onGoConsulta={() => setVistaActiva('consulta')}
          />
        )}

        {/* Suggestions Page View */}
        {vistaActiva === 'sugeridas' && (
          <div className="flex-1 flex flex-col overflow-hidden">
            {/* Header: módulo + búsqueda + ordenar */}
            <div className="flex items-center justify-between px-5 py-3 border-b border-gray-800 flex-shrink-0 gap-3 bg-ink-900/60">
              <div className="flex items-center gap-3 flex-shrink-0">
                <Lightbulb size={14} className="text-amber-400" />
                <span className="text-sm font-semibold text-white">Preguntas sugeridas</span>
                <div className="flex items-center gap-1 bg-black/30 rounded-lg p-0.5 border border-gray-700">
                  <button onClick={() => { setSuggestionTab('adpro'); setSuggestionCategory('todas') }}
                    className={`px-3 py-1 rounded-md text-xs font-mono font-medium transition-all ${suggestionTab === 'adpro' ? 'bg-azure-600 text-white' : 'text-gray-500 hover:text-gray-300'}`}>ADPRO</button>
                  <button onClick={() => { setSuggestionTab('srm'); setSuggestionCategory('todas') }}
                    className={`px-3 py-1 rounded-md text-xs font-mono font-medium transition-all ${suggestionTab === 'srm' ? 'bg-purple-600 text-white' : 'text-gray-500 hover:text-gray-300'}`}>SRM</button>
                </div>
              </div>
              <div className="flex items-center gap-2 flex-1 min-w-0">
                <div className="flex items-center gap-2 bg-ink-800 border border-gray-700 focus-within:border-azure-500/50 rounded-xl px-3 py-1.5 transition-colors flex-1 min-w-0">
                  <Search size={12} className="text-gray-500 flex-shrink-0" />
                  <input autoFocus type="text" value={suggestionSearch}
                    onChange={e => setSuggestionSearch(e.target.value)}
                    placeholder="Buscar pregunta..."
                    className="flex-1 bg-transparent text-sm text-gray-300 placeholder-gray-600 outline-none min-w-0" />
                  {suggestionSearch && <button onClick={() => setSuggestionSearch('')} className="text-gray-600 hover:text-gray-300"><X size={12} /></button>}
                </div>
                <select value={suggestionSort} onChange={e => setSuggestionSort(e.target.value)}
                  className="bg-ink-800 border border-gray-700 text-xs text-gray-400 rounded-lg px-2 py-1.5 outline-none cursor-pointer hover:border-gray-600 flex-shrink-0">
                  <option value="default">Por categoría</option>
                  <option value="uso">Más usadas</option>
                  <option value="az">A → Z</option>
                </select>
              </div>
              <button onClick={() => setVistaActiva('consulta')} title="Ir a consulta" className="text-gray-500 hover:text-gray-200 transition-colors p-1 rounded-lg hover:bg-white/10 flex-shrink-0">
                <MessageSquare size={15} />
              </button>
            </div>

            {/* Chips de categoría */}
            <div className="flex items-center gap-1.5 px-5 py-2.5 border-b border-gray-800 flex-shrink-0 overflow-x-auto bg-ink-900/40">
              {[{ key: 'top', label: '⭐ Top' }, { key: 'todas', label: 'Todas' }, ...categories.map(c => ({ key: c, label: c }))].map(({ key, label }) => {
                const isActive = suggestionCategory === key
                const color = catColors[key] || null
                const topCount = key === 'top' ? allSuggestions.filter(s => (suggestionUsage[s.pregunta] || 0) > 0).length : null
                return (
                  <button key={key} onClick={() => setSuggestionCategory(key)}
                    className={`flex-shrink-0 px-3 py-1 rounded-full text-xs font-medium transition-all border ${
                      isActive ? 'text-white border-transparent' : 'text-gray-500 border-gray-700 hover:text-gray-300 hover:border-gray-600 bg-transparent'
                    }`}
                    style={isActive ? { background: color || (key === 'top' ? '#92400e' : '#374151'), borderColor: 'transparent' } : {}}>
                    {label}{topCount !== null && topCount > 0 && <span className="ml-1 text-amber-400">{topCount}</span>}
                  </button>
                )
              })}
            </div>

            {/* Warning sin BD */}
            {!selectedDb && (
              <div className="flex items-center gap-2 px-5 py-2 bg-amber-900/20 border-b border-amber-800/30 flex-shrink-0">
                <AlertCircle size={12} className="text-amber-400 flex-shrink-0" />
                <span className="text-xs font-mono text-amber-400">Selecciona una base de datos para ejecutar sugerencias</span>
              </div>
            )}

            {/* Grid de preguntas */}
            <div className="overflow-y-auto p-5 flex-1">
              {(() => {
                const list = getSuggestionList(suggestionCategory, suggestionSearch, suggestionSort)
                // Filtros globales actualmente aplicados (para señalar cards incompatibles)
                const filtrosActivos = Object.entries(contextVars)
                  .filter(([, v]) => Array.isArray(v) && v.length > 0)
                  .map(([k]) => k)
                if (list.length === 0) return (
                  <p className="text-sm text-gray-600 font-mono text-center py-10">
                    {suggestionCategory === 'top' ? 'Aún no hay preguntas ejecutadas. Usa alguna sugerida para que aparezca aquí.' : `Sin resultados${suggestionSearch ? ` para "${suggestionSearch}"` : ''}`}
                  </p>
                )
                const grouped = (suggestionCategory === 'todas' && suggestionSort === 'default')
                if (grouped) {
                  return categories.map(cat => {
                    const catList = list.filter(s => s.categoria === cat)
                    if (!catList.length) return null
                    const color = catColors[cat] || '#6b7280'
                    return (
                      <div key={cat} className="mb-6">
                        <div className="flex items-center gap-2 mb-2.5">
                          <span className="w-2 h-2 rounded-full flex-shrink-0" style={{ background: color }} />
                          <span className="text-xs font-mono font-semibold uppercase tracking-wider" style={{ color }}>{cat}</span>
                          <span className="text-xs font-mono text-gray-700">({catList.length})</span>
                        </div>
                        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-2">
                          {catList.map((s, i) => <SuggestionCard key={i} s={s} color={color} uso={suggestionUsage[s.pregunta] || 0} selectedDb={selectedDb} filtrosActivos={filtrosActivos} onSelect={() => { if (!selectedDb) return; setVistaActiva('consulta'); handleSuggestion(s.pregunta) }} />)}
                        </div>
                      </div>
                    )
                  })
                }
                return (
                  <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-2">
                    {list.map((s, i) => {
                      const color = catColors[s.categoria] || '#6b7280'
                      return <SuggestionCard key={i} s={s} color={color} uso={suggestionUsage[s.pregunta] || 0} selectedDb={selectedDb} filtrosActivos={filtrosActivos} onSelect={() => { if (!selectedDb) return; setVistaActiva('consulta'); handleSuggestion(s.pregunta) }} />
                    })}
                  </div>
                )
              })()}
            </div>

            {/* Footer */}
            <div className="px-5 py-2 border-t border-gray-800 flex-shrink-0 flex items-center justify-between bg-ink-900/40">
              <span className="text-[11px] font-mono text-gray-600">
                {getSuggestionList(suggestionCategory, suggestionSearch, suggestionSort).length} pregunta(s)
                {suggestionSearch && ` · "${suggestionSearch}"`}
              </span>
              <button onClick={() => setVistaActiva('consulta')} className="text-[11px] font-mono text-gray-600 hover:text-gray-400 transition-colors flex items-center gap-1">
                <MessageSquare size={10} /> Ir a consulta
              </button>
            </div>
          </div>
        )}

        {/* Schema Panel */}
        {vistaActiva === 'consulta' && showSchema && schema && (
          <SchemaPanel schema={schema} vistas={vistas} onClose={() => setShowSchema(false)} />
        )}

        {/* Chat Area */}
        <div className={`flex-1 flex flex-col overflow-hidden ${vistaActiva === 'dashboard' || vistaActiva === 'sugeridas' ? 'hidden' : ''}`}>

          {/* Messages */}
          <div className="flex-1 overflow-y-auto px-6 py-6 space-y-5">
            {!selectedDb && !loadingDb && (
              <div className="flex flex-col items-center justify-center h-full gap-8 text-center">
                {/* Logo SINCO ERP grande */}
                <div className="flex flex-col items-center gap-4">
                  <div className="w-24 h-24 rounded-2xl flex items-center justify-center" style={{ background: '#1B344C', boxShadow: '0 0 40px rgba(27,52,76,0.6)' }}>
                    <SincoLogo size={52} />
                  </div>
                  <div className="flex flex-col items-center gap-1">
                    <div className="flex items-center gap-2">
                      <span className="text-white font-bold text-2xl tracking-[0.12em]">SINCO<span style={{ color: '#0AABB8' }}>ERP</span></span>
                      <span className="text-white/30 text-xs tracking-widest">+ Plus</span>
                    </div>
                    <div className="flex items-center gap-2.5 px-4 py-1.5 rounded-full" style={{ background: 'linear-gradient(90deg, #1D7478, #058C97)' }}>
                      <BarChart2 size={15} className="text-white" />
                      <span className="text-white text-sm font-medium tracking-widest">ADPRO</span>
                      <div className="w-px h-6 bg-white/25" />
                      <div className="flex flex-col leading-none gap-[3px]">
                        <span className="text-white text-[11px] font-semibold tracking-wider">Datamart</span>
                        <span className="text-white/55 text-[10px] tracking-wider">Analyzer</span>
                      </div>
                    </div>
                  </div>
                </div>

                <div>
                  <p className="text-sm text-gray-400 max-w-sm">Selecciona una base de datos del servidor <span className="text-gray-300 font-mono">YOSEMITE</span> para comenzar a analizar tus datamarts con inteligencia artificial.</p>
                </div>

                {connected === false && (
                  <div className="flex items-center gap-2 bg-red-950/40 border border-red-800/40 rounded-xl px-4 py-3">
                    <AlertCircle size={16} className="text-red-400 flex-shrink-0" />
                    <p className="text-sm text-red-300">No se puede conectar al servidor YOSEMITE. Verifica la red y autenticación Windows.</p>
                  </div>
                )}
              </div>
            )}

            {loadingSchema && (
              <div className="flex items-center gap-3 p-4 bg-ink-800 border border-gray-700 rounded-xl">
                <Loader2 size={16} className="animate-spin text-azure-400" />
                <span className="text-sm text-gray-400">Cargando schema del datamart...</span>
              </div>
            )}

            {messages.map((msg, i) => {
              const pregunta = msg.role === 'assistant' && i > 0 && messages[i - 1]?.role === 'user'
                ? messages[i - 1].content
                : null
              const sqlKey = msg.response?.sqlGenerado
              const isSaved = !!sqlKey && savedQueries.some(q => q.sql === sqlKey)
              const onSave = (pregunta && sqlKey && !isSaved)
                ? () => handleSaveQuery(pregunta, sqlKey, msg.response?.tipoRespuesta, msg.response?.grafico)
                : null
              const isFavorite = !!sqlKey && dashboards.some(d => d.paneles.some(p => p.sql === sqlKey))
              const onFavorite = (sqlKey && !isFavorite)
                ? () => setFavModal({
                    titulo: pregunta || 'Panel',
                    sql: sqlKey,
                    tipoRespuesta: msg.response?.tipoRespuesta,
                    grafico: msg.response?.grafico,
                    esPrebuilt: !!msg.response?.esPrebuilt,
                    prebuiltKey: msg.response?.esPrebuilt ? pregunta : null,
                    filtrosActivos: { ...contextVarsRef.current },
                  })
                : isFavorite ? () => {} : null
              return (
                <MessageBubble
                  key={i}
                  msg={msg}
                  devMode={devMode}
                  pregunta={pregunta}
                  onSave={onSave}
                  isSaved={isSaved}
                  usage={msg.response?.usage}
                  onFavorite={onFavorite}
                  isFavorite={isFavorite}
                />
              )
            })}

            {/* Panel flotante de historial */}
            {showHistory && (
              <div className="fixed bottom-24 left-1/2 -translate-x-1/2 w-full max-w-2xl px-4 z-40">
                <div className="bg-ink-800 border border-gray-700 rounded-2xl shadow-2xl overflow-hidden">
                  <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-700">
                    <div className="flex items-center gap-2">
                      <Clock size={13} className="text-purple-400" />
                      <span className="text-xs font-mono text-gray-400 uppercase tracking-wider">Consultas guardadas</span>
                      <span className="text-xs font-mono text-gray-600">({savedQueries.length})</span>
                    </div>
                    <button onClick={() => setShowHistory(false)} className="text-gray-600 hover:text-gray-300 text-xs">✕</button>
                  </div>
                  {savedQueries.length === 0 ? (
                    <div className="p-6 text-center">
                      <Bookmark size={24} className="text-gray-700 mx-auto mb-2" />
                      <p className="text-xs text-gray-500 font-mono">No hay consultas guardadas.</p>
                      <p className="text-xs text-gray-600 font-mono mt-1">Usa el botón <span className="text-amber-400">Guardar consulta</span> en una respuesta con SQL.</p>
                    </div>
                  ) : (
                    <div className="max-h-80 overflow-y-auto divide-y divide-gray-800">
                      {(() => {
                        const filtrosActivos = Object.entries(contextVars)
                          .filter(([, v]) => Array.isArray(v) && v.length > 0)
                          .map(([k]) => k)
                        return savedQueries.map(q => {
                          const soportados = detectarFiltrosDesdeSql(q.sql)
                          const hayIgnorados = filtrosActivosIgnorados(soportados, filtrosActivos).length > 0
                          return (
                        <div
                          key={q.id}
                          className={`flex items-start gap-3 px-4 py-3 hover:bg-ink-700 transition-colors group ${
                            hayIgnorados ? 'border-l-2 border-amber-500/40' : ''
                          }`}
                        >
                          <button
                            onClick={() => handleRunSaved(q)}
                            disabled={sending}
                            title="Ejecutar sin IA"
                            className="flex-1 text-left min-w-0"
                          >
                            <p className="text-sm text-gray-200 truncate">{q.pregunta}</p>
                            <div className="flex items-center gap-2 mt-1">
                              <span className="text-[10px] font-mono text-gray-600">{q.tipoRespuesta}</span>
                              <span className="text-[10px] font-mono text-gray-700">{new Date(q.fecha).toLocaleDateString('es-CO', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}</span>
                              <FilterSupportBadges soportados={soportados} activos={filtrosActivos} />
                            </div>
                          </button>
                          <div className="flex items-center gap-1 flex-shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                            <button
                              onClick={() => handleRunSaved(q)}
                              disabled={sending}
                              title="Ejecutar consulta guardada (sin costo IA)"
                              className="p-1.5 rounded-lg text-purple-400 hover:bg-purple-900/30 transition-colors disabled:opacity-40"
                            >
                              <PlayCircle size={14} />
                            </button>
                            <button
                              onClick={() => handleDeleteSaved(q.id)}
                              title="Eliminar del historial"
                              className="p-1.5 rounded-lg text-gray-600 hover:text-red-400 hover:bg-red-900/20 transition-colors"
                            >
                              <Trash2 size={14} />
                            </button>
                          </div>
                        </div>
                          )
                        })
                      })()}
                    </div>
                  )}
                </div>
              </div>
            )}

            <div ref={messagesEndRef} />
          </div>

          {/* Input */}
          <div className="flex-shrink-0 border-t border-gray-800 bg-ink-900/60 backdrop-blur-sm p-4">
            <div className="flex gap-2 items-end">
              <div className={`flex-1 border rounded-2xl px-4 py-3 transition-colors ${sqlMode ? 'bg-orange-950/20 border-orange-700/50 focus-within:border-orange-500/70' : 'bg-ink-800 border-gray-700 focus-within:border-azure-500/60'}`}>
                {sqlMode && (
                  <div className="flex items-center gap-1.5 mb-2">
                    <Code size={11} className="text-orange-400" />
                    <span className="text-[10px] font-mono text-orange-400 uppercase tracking-wider">SQL Directo · Ctrl+Enter para ejecutar</span>
                  </div>
                )}
                <textarea
                  ref={inputRef}
                  value={input}
                  onChange={e => setInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder={
                    !selectedDb ? 'Selecciona una base de datos primero...' :
                    sqlMode ? 'SELECT ... FROM [esquema].[tabla] WHERE ...' :
                    `Pregunta sobre ${selectedDb.nombre}... (Enter para enviar)`
                  }
                  disabled={!selectedDb || sending}
                  rows={sqlMode ? 10 : 2}
                  className={`w-full bg-transparent text-sm text-gray-200 placeholder-gray-600 resize-none outline-none leading-relaxed ${sqlMode ? 'font-mono' : 'font-sans'}`}
                  style={{ overflowY: 'auto' }}
                />
              </div>
              <div className="flex flex-col gap-2">
                <button
                  onClick={() => { setSqlMode(v => !v); setTimeout(() => inputRef.current?.focus(), 50) }}
                  title={sqlMode ? 'Cambiar a modo IA' : 'Cambiar a modo SQL directo'}
                  className={`w-11 h-11 rounded-xl border flex items-center justify-center transition-all flex-shrink-0 ${sqlMode ? 'bg-orange-600/20 border-orange-500/40 text-orange-400' : 'bg-ink-800 border-gray-700 text-gray-500 hover:text-orange-400 hover:border-orange-500/40'}`}
                >
                  <Code size={16} />
                </button>
                <button
                  onClick={() => setShowHistory(v => !v)}
                  title={`Historial de consultas guardadas (${savedQueries.length})`}
                  className={`w-11 h-11 rounded-xl border flex items-center justify-center transition-all flex-shrink-0 relative ${showHistory ? 'bg-purple-600/20 border-purple-500/40 text-purple-400' : 'bg-ink-800 border-gray-700 text-gray-500 hover:text-purple-400 hover:border-purple-500/40'}`}
                >
                  <Clock size={16} />
                  {savedQueries.length > 0 && (
                    <span className="absolute -top-1 -right-1 min-w-[16px] h-4 px-1 bg-purple-600 text-white text-[10px] font-bold rounded-full flex items-center justify-center font-mono">
                      {savedQueries.length > 99 ? '99+' : savedQueries.length}
                    </span>
                  )}
                </button>
                <button
                  onClick={() => handleSend()}
                  disabled={!selectedDb || !input.trim() || sending}
                  className={`w-11 h-11 rounded-xl disabled:opacity-30 disabled:cursor-not-allowed flex items-center justify-center transition-all flex-shrink-0 ${sqlMode ? 'bg-orange-600 hover:bg-orange-500' : 'bg-azure-600 hover:bg-azure-500'}`}
                >
                  {sending
                    ? <Loader2 size={16} className="animate-spin text-white" />
                    : <Send size={16} className="text-white" />}
                </button>
              </div>
            </div>
            <p className="text-xs text-gray-700 font-mono mt-2 text-center">
              {sqlMode
                ? 'Modo SQL Directo · Sin IA · Solo SELECT permitido'
                : `Powered by Anthropic Claude · Solo consultas SELECT · ${schema?.columnas?.length || 0} campos indexados`}
            </p>
          </div>
        </div>

      </div>

      {/* Favorite Modal */}
      {favModal && (
        <FavoriteModal
          titulo={favModal.titulo}
          dashboards={dashboards}
          onConfirm={({ panelTitulo, dashId, nuevoDashNombre }) =>
            handleAddFavorite({
              panelTitulo, dashId, nuevoDashNombre,
              sql: favModal.sql, tipoRespuesta: favModal.tipoRespuesta, grafico: favModal.grafico,
              esPrebuilt: favModal.esPrebuilt, prebuiltKey: favModal.prebuiltKey, filtrosActivos: favModal.filtrosActivos,
            })
          }
          onCancel={() => setFavModal(null)}
        />
      )}
    </div>
  )
}
