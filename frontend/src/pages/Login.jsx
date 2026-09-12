import { useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { InputText } from 'primereact/inputtext'
import { Password } from 'primereact/password'
import { useAuth } from '../context/AuthContext'

export default function Login() {
  const { login, token } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)

  // Ya logueado → a la app.
  if (token) return <Navigate to="/nueva-consulta" replace />

  const handleSubmit = async (e) => {
    e.preventDefault()
    if (!email.trim() || !password) return
    setError(null)
    setLoading(true)
    try {
      await login(email.trim(), password)
      navigate('/nueva-consulta', { replace: true })
    } catch (err) {
      setError(
        err.response?.status === 401
          ? 'Email o contraseña incorrectos.'
          : 'No se pudo iniciar sesión. Intentalo de nuevo.'
      )
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="login-page">
      {/* ── Panel de marca (izquierda) ── */}
      <div className="login-brand">
        <div className="login-brand-inner">
          <div className="login-logo">
            <span className="login-logo-icon">🚗</span>
            <span className="login-logo-text">
              Auto<em>Leads</em>
            </span>
          </div>

          <h1>Gestioná los leads de tu concesionaria</h1>
          <p className="login-tagline">
            Cargá consultas, filtrá por canal y asesor, y exportá todo a Excel
            desde un solo lugar.
          </p>

          <ul className="login-features">
            <li><i className="pi pi-check" /> Carga rápida de consultas</li>
            <li><i className="pi pi-check" /> Filtros por canal, asesor y fecha</li>
            <li><i className="pi pi-check" /> Exportación a Excel en un clic</li>
          </ul>
        </div>
      </div>

      {/* ── Formulario (derecha) ── */}
      <div className="login-form-side">
        <div className="login-card">
          <div className="login-card-badge">
            <i className="pi pi-lock" />
          </div>

          <h2>Iniciar sesión</h2>
          <p className="login-subtitle">Ingresá con tu cuenta para continuar.</p>

          <form onSubmit={handleSubmit}>
            <div className="nc-field">
              <label className="field-label field-required" htmlFor="loginEmail">Email</label>
              <InputText
                id="loginEmail"
                className="nc-input"
                value={email}
                onChange={e => setEmail(e.target.value)}
                placeholder="tu@email.com"
                type="email"
                autoComplete="username"
                autoFocus
              />
            </div>

            <div className="nc-field">
              <label className="field-label field-required" htmlFor="loginPassword">Contraseña</label>
              <Password
                inputId="loginPassword"
                inputClassName="nc-input"
                style={{ width: '100%' }}
                inputStyle={{ width: '100%' }}
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="••••••••"
                feedback={false}
                toggleMask
                autoComplete="current-password"
              />
            </div>

            {error && <div className="login-error">{error}</div>}

            <button
              className="btn-primary login-submit"
              type="submit"
              disabled={loading || !email.trim() || !password}
            >
              <i className={loading ? 'pi pi-spin pi-spinner' : 'pi pi-sign-in'} />
              {loading ? 'Ingresando...' : 'Ingresar'}
            </button>
          </form>

          <div className="login-card-footer">
            AutoLeads CRM · Acceso restringido
          </div>
        </div>
      </div>
    </div>
  )
}
