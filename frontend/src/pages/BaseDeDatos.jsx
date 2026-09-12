import { useState, useRef, useMemo } from 'react'
import { DataTable }    from 'primereact/datatable'
import { Column }       from 'primereact/column'
import { Dropdown }     from 'primereact/dropdown'
import { Calendar }     from 'primereact/calendar'
import { Toast }        from 'primereact/toast'
import { useConsultas, useCatalogos } from '../hooks/useConsultas'
import apiClient from '../api/client'

// ── Helpers ────────────────────────────────────────────────────────────────────

/** Maps canal name → CSS class for badge */
function getCanalClass(canal) {
  const key = canal?.toLowerCase().replace(/\s+/g, '') ?? ''
  const map = {
    instagram:    'canal-instagram',
    whatsapp:     'canal-whatsapp',
    facebook:     'canal-facebook',
    mercadolibre: 'canal-mercadolibre',
    web:          'canal-web',
    llamado:      'canal-llamado',
    presencial:   'canal-presencial',
    referido:     'canal-referido',
  }
  return map[key] ?? 'canal-default'
}

/** Deterministic color from asesor name */
const AVATAR_COLORS = ['#E53935', '#1565C0', '#2E7D32', '#6A1B9A', '#E65100', '#00838F', '#AD1457', '#558B2F']
function getAsesorColor(name = '') {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) | 0
  return AVATAR_COLORS[Math.abs(h) % AVATAR_COLORS.length]
}

function getAsesorInitials(name = '') {
  return name
    .split(/\s+/)
    .map(w => w[0])
    .join('')
    .substring(0, 2)
    .toUpperCase() || '?'
}

/** Human-friendly relative date string */
function formatFecha(dateStr) {
  if (!dateStr) return '—'
  const d   = new Date(dateStr)
  const now = new Date()
  const hoy = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const dDate = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  const diffDays = Math.round((hoy - dDate) / 86400000)
  const hora = d.toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' })
  if (diffDays === 0) return `Hoy, ${hora}`
  if (diffDays === 1) return `Ayer, ${hora}`
  return d.toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' }) + `, ${hora}`
}

// ── Column body templates ──────────────────────────────────────────────────────

function FechaBody({ fecha }) {
  return (
    <span style={{
      fontSize: '0.83rem',
      color: 'var(--color-text-secondary)',
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      display: 'block',
    }}>
      {formatFecha(fecha)}
    </span>
  )
}

function CanalBody({ canal }) {
  return (
    <span
      className={`canal-badge ${getCanalClass(canal)}`}
      style={{ display: 'inline-block', maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis' }}
    >
      {canal}
    </span>
  )
}

function ModeloBody({ modelo }) {
  return (
    <strong style={{
      fontSize: '0.875rem',
      letterSpacing: '-0.01em',
      display: 'block',
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
    }}>
      {modelo}
    </strong>
  )
}

function ClienteBody({ nombreCliente }) {
  return (
    <span style={{
      fontSize: '0.875rem',
      display: 'block',
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
    }}>
      {nombreCliente || '—'}
    </span>
  )
}

function TelefonoBody({ telefono }) {
  return (
    <a
      href={`tel:${telefono}`}
      style={{ color: 'var(--color-primary)', fontWeight: 600, fontSize: '0.875rem', textDecoration: 'none' }}
    >
      {telefono}
    </a>
  )
}

function AsesorBody({ asesorAsignado }) {
  return (
    <div
      title={asesorAsignado}
      style={{
        width:           32, height: 32,
        borderRadius:    '50%',
        background:      getAsesorColor(asesorAsignado),
        color:           '#FFFFFF',
        display:         'flex',
        alignItems:      'center',
        justifyContent:  'center',
        fontSize:        '0.72rem',
        fontWeight:      700,
        cursor:          'default',
      }}
    >
      {getAsesorInitials(asesorAsignado)}
    </div>
  )
}

function ObservacionesBody({ observaciones }) {
  if (!observaciones) return <span style={{ color: 'var(--color-text-muted)' }}>—</span>
  return (
    <span
      title={observaciones}
      style={{
        fontSize: '0.85rem',
        color: 'var(--color-text-secondary)',
        display: '-webkit-box',
        WebkitLineClamp: 2,
        WebkitBoxOrient: 'vertical',
        overflow: 'hidden',
        lineHeight: 1.3
      }}
    >
      {observaciones}
    </span>
  )
}

// ── Main Component ─────────────────────────────────────────────────────────────

const EMPTY_FILTROS = { canal: '', asesorAsignado: '', fechaDesde: '', fechaHasta: '' }

export default function BaseDeDatos() {
  const [filtros,     setFiltros]     = useState(EMPTY_FILTROS)
  const [downloading, setDownloading] = useState(false)
  const { catalogos }                 = useCatalogos()
  const { consultas, loading }        = useConsultas(filtros)
  const toast                         = useRef(null)

  const setFiltro = (key, val) => setFiltros(prev => ({ ...prev, [key]: val ?? '' }))

  const hasFilters = Object.values(filtros).some(Boolean)

  // Dropdown options with "all" option first
  const canalOpts  = useMemo(() =>
    [{ label: 'Todos los Canales', value: '' }, ...catalogos.canales.map(c => ({ label: c, value: c }))],
    [catalogos.canales]
  )
  const asesorOpts = useMemo(() =>
    [{ label: 'Todos los Asesores', value: '' }, ...catalogos.asesores.map(a => ({ label: a, value: a }))],
    [catalogos.asesores]
  )

  // ── Excel export ──────────────────────────────────────────────────────────
  const exportarExcel = async () => {
    setDownloading(true)
    try {
      const params = new URLSearchParams()
      if (filtros.canal)          params.append('canal',          filtros.canal)
      if (filtros.asesorAsignado) params.append('asesorAsignado', filtros.asesorAsignado)
      if (filtros.fechaDesde)     params.append('fechaDesde',     filtros.fechaDesde)
      if (filtros.fechaHasta)     params.append('fechaHasta',     filtros.fechaHasta)

      const res = await apiClient.get(
        `/api/consultas/export?${params}`,
        { responseType: 'blob' }
      )

      const url      = window.URL.createObjectURL(new Blob([res.data]))
      const link     = document.createElement('a')
      const filename = `consultas_${new Date().toISOString().slice(0, 10)}.xlsx`
      link.href      = url
      link.setAttribute('download', filename)
      document.body.appendChild(link)
      link.click()
      link.remove()
      window.URL.revokeObjectURL(url)

      toast.current.show({
        severity: 'success',
        summary:  'Exportado',
        detail:   `${consultas.length} consulta${consultas.length !== 1 ? 's' : ''} exportada${consultas.length !== 1 ? 's' : ''} a Excel.`,
        life:     3000,
      })
    } catch {
      toast.current.show({
        severity: 'error',
        summary:  'Error al exportar',
        detail:   'No se pudo generar el archivo. Verificá la conexión.',
        life:     4000,
      })
    } finally {
      setDownloading(false)
    }
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <>
      <Toast ref={toast} position="top-right" />

      {/* ── Header row ── */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 24 }}>
        <div className="page-header" style={{ margin: 0 }}>
          <h1>Base de Datos</h1>
          <p>Filtrá, analizá y exportá las consultas recibidas.</p>
        </div>
        <button
          className="btn-excel"
          onClick={exportarExcel}
          disabled={downloading || loading}
          id="exportar-excel-btn"
          type="button"
        >
          <i className={downloading ? 'pi pi-spin pi-spinner' : 'pi pi-file-excel'} />
          {downloading ? 'Exportando...' : 'Exportar a Excel'}
        </button>
      </div>

      {/* ── Filter Bar ── */}
      <div className="card" style={{ padding: '16px 22px', marginBottom: 16 }}>
        <div className="filters-bar">
          <span className="filters-label">
            <i className="pi pi-filter" style={{ fontSize: '0.9rem', color: 'var(--color-primary)' }} />
            Filtros:
          </span>

          <Dropdown
            id="filtro-canal"
            value={filtros.canal}
            options={canalOpts}
            onChange={e => setFiltro('canal', e.value)}
            className="filter-dropdown"
            style={{ minWidth: 200 }}
            placeholder="Todos los Canales"
          />

          <Dropdown
            id="filtro-asesor"
            value={filtros.asesorAsignado}
            options={asesorOpts}
            onChange={e => setFiltro('asesorAsignado', e.value)}
            className="filter-dropdown"
            style={{ minWidth: 200 }}
            placeholder="Todos los Asesores"
          />

          <Calendar
            id="filtro-fecha-desde"
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
            id="filtro-fecha-hasta"
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
              onClick={() => setFiltros(EMPTY_FILTROS)}
              type="button"
            >
              <i className="pi pi-times" />
              Limpiar
            </button>
          )}
        </div>
      </div>

      {/* ── DataTable Full Width ── */}
      <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
        <DataTable
          value={consultas}
          loading={loading}
          paginator
          rows={10}
          rowsPerPageOptions={[10, 25, 50, 100]}
          emptyMessage={
            <div style={{ textAlign: 'center', padding: '40px 0', color: 'var(--color-text-muted)' }}>
              <i className="pi pi-inbox" style={{ fontSize: '2rem', display: 'block', marginBottom: 10 }} />
              No se encontraron consultas con los filtros aplicados.
            </div>
          }
          tableStyle={{ width: '100%', tableLayout: 'fixed' }}
          removableSort
        >
          <Column
            field="fecha"
            header="Fecha"
            sortable
            body={row => <FechaBody fecha={row.fecha} />}
            style={{ width: '12%' }}
          />
          <Column
            field="canal"
            header="Canal"
            sortable
            body={row => <CanalBody canal={row.canal} />}
            style={{ width: '11%' }}
          />
          <Column
            field="modelo"
            header="Modelo"
            sortable
            body={row => <ModeloBody modelo={row.modelo} />}
            style={{ width: '14%' }}
          />
          <Column
            field="nombreCliente"
            header="Cliente"
            sortable
            body={row => <ClienteBody nombreCliente={row.nombreCliente} />}
            style={{ width: '15%' }}
          />
          <Column
            field="telefono"
            header="Teléfono"
            body={row => <TelefonoBody telefono={row.telefono} />}
            style={{ width: '12%' }}
          />
          <Column
            field="ciudad"
            header="Ciudad"
            body={row => <span style={{ fontSize: '0.85rem', color: 'var(--color-text-secondary)', display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{row.ciudad || '—'}</span>}
            style={{ width: '11%' }}
          />
          <Column
            field="asesorAsignado"
            header="Asesor"
            sortable
            body={row => <AsesorBody asesorAsignado={row.asesorAsignado} />}
            style={{ width: '6%', textAlign: 'center' }}
          />
          <Column
            field="observaciones"
            header="Observaciones"
            body={row => <ObservacionesBody observaciones={row.observaciones} />}
            style={{ width: '19%' }}
          />
        </DataTable>

        <div className="table-footer">
          {loading
            ? 'Cargando...'
            : `Mostrando ${consultas.length} consulta${consultas.length !== 1 ? 's' : ''}${hasFilters ? ' (filtrado)' : ''}`
          }
        </div>
      </div>
    </>
  )
}
