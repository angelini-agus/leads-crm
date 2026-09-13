import { useState, useEffect, useCallback } from 'react'
import apiClient from '../api/client'

/**
 * CRUD de usuarios (solo admin). Carga la lista y expone acciones que
 * recargan la tabla al terminar.
 */
export function useUsuarios() {
  const [usuarios, setUsuarios] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const cargar = useCallback(async () => {
    try {
      const res = await apiClient.get('/api/usuarios')
      setUsuarios(res.data)
      setError(null)
    } catch (err) {
      setError(err)
      throw err
    }
  }, [])

  useEffect(() => {
    cargar()
      .catch(() => { /* el error queda expuesto via `error` */ })
      .finally(() => setLoading(false))
  }, [cargar])

  const crearUsuario = async (payload) => {
    await apiClient.post('/api/usuarios', payload)
    await cargar()
  }

  const actualizarUsuario = async (id, payload) => {
    await apiClient.put(`/api/usuarios/${id}`, payload)
    await cargar()
  }

  const cambiarPassword = async (id, password) => {
    await apiClient.put(`/api/usuarios/${id}/password`, { password })
  }

  const alternarEstadoUsuario = async (usuario) => {
    await apiClient.put(`/api/usuarios/${usuario.id}`, {
      nombre: usuario.nombre,
      email: usuario.email,
      rol: usuario.rol,
      activo: !usuario.activo,
    })
    await cargar()
  }

  return { usuarios, loading, error, crearUsuario, actualizarUsuario, cambiarPassword, alternarEstadoUsuario }
}
