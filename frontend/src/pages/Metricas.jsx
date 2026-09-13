import { useState, useRef, useMemo, useEffect } from 'react'
import { Calendar } from 'primereact/calendar'
import { Toast } from 'primereact/toast'
import { Chart } from 'primereact/chart'
import { useMetricas } from '../hooks/useMetricas'

const ASESOR_COLORS = ['#E53935', '#1565C0', '#2E7D32', '#6A1B9A', '#E65100', '#00838F', '#AD1457', '#558B2F']

const CANAL_VARS = {
  instagram:    '--canal-instagram-fg',
  whatsapp:     '--canal-whatsapp-fg',
  facebook:     '--canal-facebook-fg',
  mercadolibre: '--canal-mercadolibre-fg',
  web:          '--canal-web-fg',
  llamado:      '--canal-llamado-fg',
  presencial:   '--canal-presencial-fg',
  referido:     '--canal-referido-fg',
}

function cssVar(name, fallback) {
  if (typeof window === 'undefined') return fallback
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return v || fallback
}

function canalColor(canal) {
  const key = (canal || '').toLowerCase().replace(/\s+/g, '')
  return cssVar(CANAL_VARS[key] || '--color-text-muted', '#6B7280')
}

function formatDia(fecha) {
  return new Date(fecha).toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit' })
}

const baseOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { display: false } },
  scales: {
    x: { grid: { display: false } },
    y: { beginAtZero: true, ticks: { precision: 0 } },
  },
}

export default function Metricas() {
  const [filtros, setFiltros] = useState({ fechaDesde: '', fechaHasta: '' })
  const { data, loading, error } = useMetricas(filtros)
  const toast = useRef(null)

  useEffect(() => {
    if (error) {
      toast.current?.show({ severity: 'error', summary: 'Error', detail: 'No se pudieron cargar las métricas.', life: 4000 })
    }
  }, [error])

  const setFiltro = (key, val) => setFiltros(prev => ({ ...prev, [key]: val ?? '' }))
  const hasFilters = Boolean(filtros.fechaDesde || filtros.fechaHasta)

  const primary = cssVar('--color-primary', '#E53935')

  const canalChart = useMemo(() => {
    if (!data) return null
    return {
      labels: data.porCanal.map(c => c.canal),
      datasets: [{
        data: data.porCanal.map(c => c.cantidad),
        backgroundColor: data.porCanal.map(c => canalColor(c.canal)),
        borderRadius: 8,
      }],
    }
  }, [data])

  const diaChart = useMemo(() => {
    if (!data) return null
    return {
      labels: data.porDia.map(d => formatDia(d.fecha)),
      datasets: [{
        label: 'Consultas',
        data: data.porDia.map(d => d.cantidad),
        borderColor: primary,
        backgroundColor: `${primary}22`,
        fill: true,
        tension: 0.35,
        pointRadius: 3,
        pointBackgroundColor: primary,
      }],
    }
  }, [data, primary])

  const modeloChart = useMemo(() => {
    if (!data) return null
    return {
      labels: data.porModelo.map(m => m.modelo),
      datasets: [{
        data: data.porModelo.map(m => m.cantidad),
        backgroundColor: primary,
        borderRadius: 8,
      }],
    }
  }, [data, primary])

  const asesorChart = useMemo(() => {
    if (!data) return null
    return {
      labels: data.porAsesor.map(a => a.asesor),
      datasets: [{
        data: data.porAsesor.map(a => a.cantidad),
        backgroundColor: data.porAsesor.map((_, i) => ASESOR_COLORS[i % ASESOR_COLORS.length]),
        borderWidth: 2,
        borderColor: '#FFFFFF',
      }],
    }
  }, [data])

  const kpis = useMemo(() => {
    if (!data) return []
    const canalTop = data.porCanal[0]
    const asesorTop = data.porAsesor[0]
    const modeloTop = data.porModelo[0]
    return [
      { icon: 'pi pi-inbox', color: '#FFEBEE', iconColor: '#E53935', label: 'Total consultas', value: data.totalConsultas, sub: hasFilters ? 'en el rango' : 'en el período' },
      { icon: 'pi pi-share-alt', color: '#F3E5F5', iconColor: '#7B1FA2', label: 'Canal más usado', value: canalTop?.canal ?? '—', sub: canalTop ? `${canalTop.cantidad} consultas` : '' },
      { icon: 'pi pi-user', color: '#E3F2FD', iconColor: '#1565C0', label: 'Asesor destacado', value: asesorTop?.asesor ?? '—', sub: asesorTop ? `${asesorTop.cantidad} leads` : '' },
      { icon: 'pi pi-car', color: '#E8F5E9', iconColor: '#2E7D32', label: 'Modelo top', value: modeloTop?.modelo ?? '—', sub: modeloTop ? `${modeloTop.cantidad} consultas` : '' },
    ]
  }, [data, hasFilters])

  return (
    <>
      <Toast ref={toast} position="top-right" />

      <div className="page-header" style={{ marginBottom: 20 }}>
        <h1>Métricas</h1>
        <p>Analizá el volumen de consultas por canal, asesor, modelo y tendencia diaria.</p>
      </div>

      {/* ── Filtro de fechas ── */}
      <div className="card" style={{ padding: '16px 22px', marginBottom: 24 }}>
        <div className="filters-bar">
          <span className="filters-label">
            <i className="pi pi-calendar" style={{ fontSize: '0.9rem', color: 'var(--color-primary)' }} />
            Período:
          </span>

          <Calendar
            value={filtros.fechaDesde ? new Date(filtros.fechaDesde + 'T12:00:00') : null}
            onChange={e => setFiltro('fechaDesde', e.value ? e.value.toISOString().slice(0, 10) : '')}
            placeholder="Desde"
            dateFormat="dd/mm/yy"
            showIcon
            showButtonBar
            className="filter-calendar"
            style={{ width: 170 }}
          />

          <Calendar
            value={filtros.fechaHasta ? new Date(filtros.fechaHasta + 'T12:00:00') : null}
            onChange={e => setFiltro('fechaHasta', e.value ? e.value.toISOString().slice(0, 10) : '')}
            placeholder="Hasta"
            dateFormat="dd/mm/yy"
            showIcon
            showButtonBar
            className="filter-calendar"
            style={{ width: 170 }}
          />

          {hasFilters && (
            <button
              className="btn-secondary filter-clear-btn"
              onClick={() => setFiltros({ fechaDesde: '', fechaHasta: '' })}
              type="button"
            >
              <i className="pi pi-times" />
              Limpiar
            </button>
          )}
        </div>
      </div>

      {/* ── KPIs ── */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '20px', marginBottom: 24 }}>
        {kpis.map(k => (
          <div className="card" key={k.label} style={{ padding: '22px 24px', display: 'flex', alignItems: 'center', gap: '16px' }}>
            <span className="section-icon" style={{ background: k.color, color: k.iconColor, width: 48, height: 48 }}>
              <i className={k.icon} />
            </span>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-secondary)' }}>{k.label}</div>
              <div style={{
                fontSize: '1.35rem', fontWeight: 800, color: 'var(--color-text-primary)',
                whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis'
              }}>
                {k.value}
              </div>
              {k.sub && <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>{k.sub}</div>}
            </div>
          </div>
        ))}
      </div>

      {/* ── Gráficos ── */}
      {loading && !data ? (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '240px', color: 'var(--color-text-muted)', gap: '10px' }}>
          <i className="pi pi-spin pi-spinner" style={{ fontSize: '1.2rem' }} />
          Cargando métricas...
        </div>
      ) : data ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(420px, 100%), 1fr))', gap: '24px' }}>
          <div className="card" style={{ padding: '20px 22px' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 700, margin: '0 0 16px' }}>Consultas por canal</h3>
            <div style={{ height: 280 }}>
              <Chart type="bar" data={canalChart} options={baseOptions} style={{ height: '100%' }} />
            </div>
          </div>

          <div className="card" style={{ padding: '20px 22px' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 700, margin: '0 0 16px' }}>Tendencia diaria</h3>
            <div style={{ height: 280 }}>
              <Chart type="line" data={diaChart} options={baseOptions} style={{ height: '100%' }} />
            </div>
          </div>

          <div className="card" style={{ padding: '20px 22px' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 700, margin: '0 0 16px' }}>Top 5 modelos</h3>
            <div style={{ height: 280 }}>
              <Chart type="bar" data={modeloChart} options={{ ...baseOptions, indexAxis: 'y' }} style={{ height: '100%' }} />
            </div>
          </div>

          <div className="card" style={{ padding: '20px 22px' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 700, margin: '0 0 16px' }}>Distribución por asesor</h3>
            <div style={{ height: 280 }}>
              <Chart
                type="doughnut"
                data={asesorChart}
                options={{ responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'right' } } }}
                style={{ height: '100%' }}
              />
            </div>
          </div>
        </div>
      ) : null}
    </>
  )
}
