import { createContext, useContext, useCallback, useEffect, useMemo, useState } from 'react'
import apiClient from '../api/client'

const AuthContext = createContext(null)

/**
 * Proveedor de autenticación. El token vive en una cookie HttpOnly que maneja
 * el backend, así que acá solo guardamos la identidad en memoria (user). Al
 * arrancar se restaura la sesión consultando GET /api/auth/me.
 * Expone { user, loading, login, logout }.
 */
export function AuthProvider({ children }) {
  const [user, setUser] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let isMounted = true

    apiClient
      .get('/api/auth/me')
      .then(res => {
        if (isMounted) setUser({ nombre: res.data.nombre, rol: res.data.rol })
      })
      .catch(() => {
        if (isMounted) setUser(null)
      })
      .finally(() => {
        if (isMounted) setLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [])

  const login = useCallback(async (email, password) => {
    const res = await apiClient.post('/api/auth/login', { email, password })
    const { nombre, rol } = res.data
    setUser({ nombre, rol })
    return { nombre, rol }
  }, [])

  const logout = useCallback(async () => {
    try {
      await apiClient.post('/api/auth/logout')
    } catch {
      /* aunque falle el request, cerramos la sesión local */
    }
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({ user, loading, login, logout }),
    [user, loading, login, logout]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth debe usarse dentro de <AuthProvider>')
  return ctx
}
