import { NavLink, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'

const NAV_GROUPS = [
  {
    section: 'GESTIÓN DIARIA',
    adminOnly: false,
    items: [
      {
        to:    '/nueva-consulta',
        label: 'Ingresar Consulta',
        icon:  'pi pi-plus-circle',
      },
      {
        to:    '/base-de-datos',
        label: 'Base de Datos',
        icon:  'pi pi-database',
      },
    ],
  },
  {
    section: 'ADMINISTRACIÓN',
    adminOnly: true,
    items: [
      {
        to:    '/configuracion',
        label: 'Configuración Maestros',
        icon:  'pi pi-cog',
      },
      {
        to:    '/metricas',
        label: 'Métricas',
        icon:  'pi pi-chart-bar',
      },
    ],
  },
]

function NavItem({ item, collapsed }) {
  if (item.disabled) {
    return (
      <div
        className="sidebar-nav-item"
        style={{ opacity: 0.38, cursor: 'not-allowed', userSelect: 'none' }}
        title={`${item.label} (Próximamente)`}
      >
        <span className="nav-icon-wrap">
          <i className={item.icon} />
        </span>
        {!collapsed && <span className="nav-label-text">{item.label}</span>}
      </div>
    )
  }

  return (
    <NavLink
      to={item.to}
      title={collapsed ? item.label : undefined}
      className={({ isActive }) => `sidebar-nav-item${isActive ? ' active' : ''}`}
    >
      <span className="nav-icon-wrap">
        <i className={item.icon} />
      </span>
      {!collapsed && <span className="nav-label-text">{item.label}</span>}
    </NavLink>
  )
}

export default function Sidebar({ collapsed, onToggle }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const rol = user?.rol
  const isAdmin = rol === 'admin'
  const rolLabel = isAdmin ? 'Administrador' : 'Asesor'

  // Iniciales para el avatar (primeras letras del nombre).
  const initials = (user?.nombre ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .map(p => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase() || '??'

  // Los asesores no ven el grupo ADMINISTRACIÓN.
  const groups = NAV_GROUPS.filter(g => !g.adminOnly || isAdmin)

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <aside className={`sidebar${collapsed ? ' collapsed' : ''}`}>
      {/* ── Logo + Toggle Button ── */}
      <div className="sidebar-logo">
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          <span className="sidebar-logo-icon">🚗</span>
          {!collapsed && (
            <span className="sidebar-logo-text">
              Auto<em>Leads</em>
            </span>
          )}
        </div>
        <button
          className="btn-toggle-sidebar"
          onClick={onToggle}
          title={collapsed ? "Ampliar menú" : "Guardar menú"}
          aria-label={collapsed ? "Ampliar menú lateral" : "Guardar menú lateral"}
          type="button"
        >
          <i className={collapsed ? "pi pi-chevron-right" : "pi pi-chevron-left"} />
        </button>
      </div>

      {/* ── Navigation ── */}
      <nav className="sidebar-nav">
        {groups.map(group => (
          <div key={group.section}>
            {!collapsed && <div className="sidebar-section-label">{group.section}</div>}
            {group.items.map(item => (
              <NavItem key={item.label} item={item} collapsed={collapsed} />
            ))}
          </div>
        ))}
      </nav>

      {/* ── User Footer ── */}
      <div className="sidebar-footer">
        <div className="user-avatar" title={`${user?.nombre ?? ''} (${rolLabel})`}>{initials}</div>
        {!collapsed && (
          <div className="user-info">
            <div className="user-name">{user?.nombre ?? 'Invitado'}</div>
            <div className="user-role">{rolLabel}</div>
          </div>
        )}
        <button
          className="btn-icon-ghost"
          title="Cerrar sesión"
          aria-label="Cerrar sesión"
          type="button"
          onClick={handleLogout}
        >
          <i className="pi pi-sign-out" style={{ fontSize: '0.9rem' }} />
        </button>
      </div>
    </aside>
  )
}
