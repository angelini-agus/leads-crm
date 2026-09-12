import { createContext, useContext, useCallback, useEffect, useMemo, useState } from 'react'
import apiClient, { AUTH_STORAGE_KEY } from '../api/client'

const AuthContext = createContext(null)

/**
 * Proveedor de autenticación. Guarda el token en memoria (state) y en
 * localStorage para sobrevivir a un refresh. Expone { user, token, loading,
 * login, logout }.
 */
export function AuthProvider({ children }) {
  const [user, setUser] = useState(null)
  const [token, setToken] = useState(null)
  const [loading, setLoading] = useState(true)

  // Restaura la sesión guardada al arrancar la app.
  useEffect(() => {
    try {
      const raw = localStorage.getItem(AUTH_STORAGE_KEY)
      if (raw) {
        const parsed = JSON.parse(raw)
        if (parsed.token) {
          setToken(parsed.token)
          setUser({ nombre: parsed.nombre, rol: parsed.rol })
        }
      }
    } catch {
      localStorage.removeItem(AUTH_STORAGE_KEY)
    } finally {
      setLoading(false)
    }
  }, [])

  const login = useCallback(async (email, password) => {
    const res = await apiClient.post('/api/auth/login', { email, password })
    const { token, nombre, rol } = res.data
    localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify({ token, nombre, rol }))
    setToken(token)
    setUser({ nombre, rol })
    return { nombre, rol }
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(AUTH_STORAGE_KEY)
    setToken(null)
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({ user, token, loading, login, logout }),
    [user, token, loading, login, logout]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth debe usarse dentro de <AuthProvider>')
  return ctx
}
