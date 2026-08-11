import axios from 'axios'

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' }
})

const enc = encodeURIComponent

export const datamartApi = {
  ping: () => api.get('/databases/ping').then(r => r.data),
  getDatabases: () => api.get('/databases').then(r => r.data),
  refreshDatabases: () => api.post('/databases/refresh').then(r => r.data),
  getSchema: (database) => api.get(`/schema/${enc(database)}`).then(r => r.data),
  getViews: (database) => api.get(`/schema/${enc(database)}/views`).then(r => r.data),

  analyze: (payload) => api.post('/analyze', payload).then(r => r.data),
  executeQuery: (database, sql) => api.post('/query', { database, sql }).then(r => r.data),
  executeWithFilters: (database, sql, filtros) => api.post('/analyze/execute', { database, sql, filtros }).then(r => r.data),
  getFilterValues: (database, tipo) => api.get(`/filters/${enc(database)}/${tipo}`).then(r => r.data),

  // ─── Contexto ERP ───────────────────────────────────────────────────────────
  // Si la app se abrio desde un ERP, el datamart lo dicta dbo.ADPConfig y no se
  // elige a mano. En standalone `modo` viene 'standalone' y todo sigue igual.
  erp: {
    getContext: () => api.get('/erp/context').then(r => r.data),
    getDatamarts: () => api.get('/erp/datamarts').then(r => r.data),
    refreshContext: () => api.post('/erp/context/refresh').then(r => r.data),
  },

  // ─── Almacen compartido ─────────────────────────────────────────────────────
  // Vive dentro de cada datamart (esquema ADP_DTM_APP), asi que todo cuelga
  // de {database}. Reemplaza al localStorage del navegador.
  store: {
    status: (database) => api.get(`/store/status/${enc(database)}`).then(r => r.data),
    importar: (database, payload) => api.post(`/store/import/${enc(database)}`, payload).then(r => r.data),
  },

  consultas: {
    listar: (database, buscar) =>
      api.get(`/savedqueries/${enc(database)}`, { params: buscar ? { buscar } : {} }).then(r => r.data),
    obtener: (database, id) => api.get(`/savedqueries/${enc(database)}/${id}`).then(r => r.data),
    crear: (database, payload) => api.post(`/savedqueries/${enc(database)}`, payload).then(r => r.data),
    actualizar: (database, id, payload) => api.put(`/savedqueries/${enc(database)}/${id}`, payload).then(r => r.data),
    borrar: (database, id) => api.delete(`/savedqueries/${enc(database)}/${id}`).then(r => r.data),
  },

  dashboards: {
    listar: (database) => api.get(`/dashboards/${enc(database)}`).then(r => r.data),
    obtener: (database, id) => api.get(`/dashboards/${enc(database)}/${id}`).then(r => r.data),
    crear: (database, payload) => api.post(`/dashboards/${enc(database)}`, payload).then(r => r.data),
    actualizar: (database, id, payload) => api.put(`/dashboards/${enc(database)}/${id}`, payload).then(r => r.data),
    borrar: (database, id) => api.delete(`/dashboards/${enc(database)}/${id}`).then(r => r.data),

    agregarPanel: (database, dashId, payload) =>
      api.post(`/dashboards/${enc(database)}/${dashId}/panels`, payload).then(r => r.data),
    actualizarPanel: (database, dashId, panelId, payload) =>
      api.put(`/dashboards/${enc(database)}/${dashId}/panels/${panelId}`, payload).then(r => r.data),
    quitarPanel: (database, dashId, panelId) =>
      api.delete(`/dashboards/${enc(database)}/${dashId}/panels/${panelId}`).then(r => r.data),
    reordenarPaneles: (database, dashId, panelIds) =>
      api.put(`/dashboards/${enc(database)}/${dashId}/panels/order`, { panelIds }).then(r => r.data),
  },

  // Catalogo de preguntas sugeridas. Antes vivia hardcodeado en App.jsx y el
  // contador de usos en localStorage; ahora sale del datamart, asi la app web y el
  // conector de Claude ven exactamente lo mismo.
  sugeridas: {
    listar: (database, modulo, incluirOcultas = false) =>
      api.get(`/suggestedquestions/${enc(database)}`, {
        params: { ...(modulo ? { modulo } : {}), ...(incluirOcultas ? { incluirOcultas: true } : {}) }
      }).then(r => r.data),
    crear: (database, payload) => api.post(`/suggestedquestions/${enc(database)}`, payload).then(r => r.data),
    actualizar: (database, id, payload) =>
      api.put(`/suggestedquestions/${enc(database)}/${id}`, payload).then(r => r.data),
    borrar: (database, id) => api.delete(`/suggestedquestions/${enc(database)}/${id}`).then(r => r.data),
    // Se dispara al lanzar una sugerida. No se espera la respuesta ni se muestra
    // error: es telemetria, no puede frenar la consulta del usuario.
    marcarUso: (database, id) => api.post(`/suggestedquestions/${enc(database)}/${id}/uso`).then(r => r.data),
  },

  filterPresets: {
    listar: (database) => api.get(`/filterpresets/${enc(database)}`).then(r => r.data),
    crear: (database, payload) => api.post(`/filterpresets/${enc(database)}`, payload).then(r => r.data),
    borrar: (database, id) => api.delete(`/filterpresets/${enc(database)}/${id}`).then(r => r.data),
  },
}

export default datamartApi
