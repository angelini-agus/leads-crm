import axios from 'axios'

/**
 * Clave de localStorage donde se persiste la sesión (token + nombre + rol).
 * La escribe AuthContext y la lee este interceptor para armar el header.
 */
export const AUTH_STORAGE_KEY = 'autoleads_auth'

/**
 * Axios instance pre-configured to point at the backend API.
 * In development, Vite proxies /api to http://localhost:5000,
 * so we can use relative paths in dev and absolute in production.
 *
 * Set VITE_API_URL in Cloudflare Pages env vars for production.
 */
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '',
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

// Request interceptor: attach the JWT (Authorization: Bearer ...) if present.
apiClient.interceptors.request.use(config => {
  const raw = localStorage.getItem(AUTH_STORAGE_KEY)
  if (raw) {
    try {
      const { token } = JSON.parse(raw)
      if (token) config.headers.Authorization = `Bearer ${token}`
    } catch {
      /* ignore malformed storage */
    }
  }
  return config
})

// Response interceptor: on 401 clear the session and bounce to /login.
apiClient.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401) {
      localStorage.removeItem(AUTH_STORAGE_KEY)
      if (!window.location.pathname.startsWith('/login')) {
        window.location.assign('/login')
      }
    }
    if (import.meta.env.DEV) {
      console.error('[API Error]', error.response?.status, error.config?.url, error.message)
    }
    return Promise.reject(error)
  }
)

export default apiClient
