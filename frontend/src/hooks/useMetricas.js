import { useState, useEffect } from 'react'
import apiClient from '../api/client'

/**
 * Carga las métricas agregadas desde /api/consultas/metricas.
 * Re-consulta cada vez que cambian los filtros de fecha.
 * @param {object} filtros - { fechaDesde, fechaHasta }
 */
export function useMetricas(filtros) {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  useEffect(() => {
    let isMounted = true
    const controller = new AbortController()

    setLoading(true)
    setError(null)

    const params = new URLSearchParams()
    if (filtros.fechaDesde) params.append('fechaDesde', filtros.fechaDesde)
    if (filtros.fechaHasta) params.append('fechaHasta', filtros.fechaHasta)

    apiClient
      .get(`/api/consultas/metricas?${params}`, { signal: controller.signal })
      .then(res => {
        if (isMounted) setData(res.data)
      })
      .catch(err => {
        if (err.name !== 'CanceledError' && err.name !== 'AbortError' && isMounted) {
          console.error('Error cargando métricas:', err)
          setError(err)
        }
      })
      .finally(() => {
        if (isMounted) setLoading(false)
      })

    return () => {
      isMounted = false
      controller.abort()
    }
  }, [filtros.fechaDesde, filtros.fechaHasta])

  return { data, loading, error }
}
