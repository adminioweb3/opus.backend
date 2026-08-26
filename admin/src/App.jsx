import { useState, useEffect } from 'react'
import './index.css'
import LoginPage from './pages/LoginPage'
import Dashboard from './pages/Dashboard'

const API_BASE = import.meta.env.VITE_API_BASE || 'http://localhost:8088'

export default function App() {
  const [isAuthenticated, setIsAuthenticated] = useState(false)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const token = localStorage.getItem('admin_token')

    if (!token) {
      setLoading(false)
      return
    }

    const verifySession = async () => {
      try {
        const response = await fetch(`${API_BASE}/api/Admin/session`, {
          headers: { Authorization: `Bearer ${token}` },
        })

        if (!response.ok) {
          throw new Error('Invalid session')
        }

        setIsAuthenticated(true)
      } catch {
        localStorage.removeItem('admin_token')
      } finally {
        setLoading(false)
      }
    }

    verifySession()
  }, [])

  const handleLogin = (token) => {
    localStorage.setItem('admin_token', token)
    setIsAuthenticated(true)
  }

  const handleLogout = () => {
    localStorage.removeItem('admin_token')
    setIsAuthenticated(false)
  }

  if (loading) {
    return <div className="flex items-center justify-center h-screen">Loading...</div>
  }

  return isAuthenticated ? (
    <Dashboard onLogout={handleLogout} />
  ) : (
    <LoginPage onLogin={handleLogin} />
  )
}
