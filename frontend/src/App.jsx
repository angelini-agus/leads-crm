import { useState, Suspense, lazy } from 'react'
import { Routes, Route, Navigate, useLocation } from 'react-router-dom'
import Sidebar from './components/Sidebar'
import TopBar from './components/TopBar'
import { useAuth } from './context/AuthContext'

// Lazy load pages for faster initial load
const NuevaConsulta = lazy(() => import('./pages/NuevaConsulta'))
const BaseDeDatos   = lazy(() => import('./pages/BaseDeDatos'))
const Configuracion = lazy(() => import('./pages/Configuracion'))
const Metricas      = lazy(() => import('./pages/Metricas'))
const Login         = lazy(() => import('./pages/Login'))

function PageLoader() {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      height: '60vh', color: '#9CA3AF', fontSize: '0.9rem', gap: '10px'
    }}>
      <i className="pi pi-spin pi-spinner" style={{ fontSize: '1.2rem' }} />
      Cargando...
    </div>
  )
}

// Redirige a /login si no hay sesión (o mientras se restaura).
function RequireAuth({ children }) {
  const { user, loading } = useAuth()
  if (loading) return <PageLoader />
  if (!user) return <Navigate to="/login" replace />
  return children
}

// Redirige a /nueva-consulta si el rol no es admin.
function RequireAdmin({ children }) {
  const { user } = useAuth()
  if (user?.rol !== 'admin') return <Navigate to="/nueva-consulta" replace />
  return children
}

function Layout() {
  const { pathname } = useLocation()
  const [collapsed, setCollapsed] = useState(() => {
    return localStorage.getItem('autoleads_sidebar_collapsed') === 'true'
  })

  const toggleSidebar = () => {
    setCollapsed(prev => {
      const next = !prev
      localStorage.setItem('autoleads_sidebar_collapsed', String(next))
      return next
    })
  }

  // NuevaConsulta usa su propio layout 100vh — no queremos overflow en page-content
  const noScroll = pathname === '/nueva-consulta' || pathname === '/'

  return (
    <div className="app-layout">
      <Sidebar collapsed={collapsed} onToggle={toggleSidebar} />
      <div className={`main-content${collapsed ? ' collapsed' : ''}`}>
        <TopBar collapsed={collapsed} onToggle={toggleSidebar} />
        <main className={`page-content${noScroll ? ' page-no-scroll' : ''}`}>
          <Suspense fallback={<PageLoader />}>
            <Routes>
              <Route path="/"               element={<Navigate to="/nueva-consulta" replace />} />
              <Route path="/nueva-consulta" element={<NuevaConsulta />} />
              <Route path="/base-de-datos"  element={<BaseDeDatos />} />
              <Route path="/configuracion"  element={<RequireAdmin><Configuracion /></RequireAdmin>} />
              <Route path="/metricas"      element={<RequireAdmin><Metricas /></RequireAdmin>} />
              <Route path="*"               element={<Navigate to="/nueva-consulta" replace />} />
            </Routes>
          </Suspense>
        </main>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="*" element={<RequireAuth><Layout /></RequireAuth>} />
    </Routes>
  )
}
