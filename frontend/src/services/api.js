import axios from 'axios'

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' }
})

export const datamartApi = {
  ping: () => api.get('/databases/ping').then(r => r.data),
  getDatabases: () => api.get('/databases').then(r => r.data),
  refreshDatabases: () => api.post('/databases/refresh').then(r => r.data),
  getSchema: (database) => api.get(`/schema/${encodeURIComponent(database)}`).then(r => r.data),
  getViews: (database) => api.get(`/schema/${encodeURIComponent(database)}/views`).then(r => r.data),

  analyze: (payload) => api.post('/analyze', payload).then(r => r.data),
  executeQuery: (database, sql) => api.post('/query', { database, sql }).then(r => r.data),
  executeWithFilters: (database, sql, filtros) => api.post('/analyze/execute', { database, sql, filtros }).then(r => r.data),
  getFilterValues: (database, tipo) => api.get(`/filters/${encodeURIComponent(database)}/${tipo}`).then(r => r.data),
}

export default datamartApi
