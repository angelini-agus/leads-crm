import axios from 'axios'

/**
 * Axios instance pre-configured to point at the backend API.
 * In development, Vite proxies /api to http://localhost:5000,
 * so we can use relative paths in dev and absolute in production.
 *
 * Set VITE_API_URL in Cloudflare Pages env vars for production.
 *
 * Auth: el JWT viaja en una cookie HttpOnly que setea el backend al loguear.
 * El frontend nunca lo lee ni lo guarda (no hay token en localStorage), así
 * que un XSS no puede exfiltrarlo. `withCredentials` es lo que envía la cookie.
 */
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
  timeout: 15000,
})

// Response interceptor: on 401 bounce to /login (la cookie ya no sirve).
apiClient.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401) {
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
