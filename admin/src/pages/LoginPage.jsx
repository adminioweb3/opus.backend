import { useState } from 'react'
import { Lock } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Alert, AlertDescription } from '@/components/ui/alert'

const ADMIN_USERNAME = 'admin'
const ADMIN_PASSWORD = 'pass@123'

export default function LoginPage({ onLogin }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const handleSubmit = (e) => {
    e.preventDefault()
    setError('')
    setLoading(true)

    setTimeout(() => {
      if (username === ADMIN_USERNAME && password === ADMIN_PASSWORD) {
        localStorage.setItem('admin_token', 'admin-session-' + Date.now())
        onLogin()
      } else {
        setError('Invalid username or password')
      }
      setLoading(false)
    }, 500)
  }

  return (
    <div className="min-h-screen bg-linear-to-br from-blue-50 to-indigo-100 flex items-center justify-center p-4">
      <Card className="w-full max-w-md">
        <CardHeader className="text-center">
          <div className="flex justify-center mb-2">
            <div className="bg-primary p-3 rounded-full">
              <Lock className="w-6 h-6 text-primary-foreground" />
            </div>
          </div>
          <CardTitle className="text-2xl">Admin Panel</CardTitle>
          <CardDescription>Sign in to manage users and data</CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4" id="login-form">
            <div className="space-y-1.5">
              <Label htmlFor="username">Username</Label>
              <Input
                id="username"
                type="text"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="admin"
                disabled={loading}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••"
                disabled={loading}
              />
            </div>

            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}

            <Button type="submit" disabled={loading} className="w-full">
              {loading ? 'Signing in...' : 'Sign In'}
            </Button>
          </form>
        </CardContent>

        <CardFooter>
          <div className="w-full p-3 bg-muted rounded-lg">
            <p className="text-xs text-muted-foreground text-center">
              <span className="font-semibold">Demo Credentials:</span>
              <br />
              Username: <code className="text-foreground">admin</code>
              <br />
              Password: <code className="text-foreground">pass@123</code>
            </p>
          </div>
        </CardFooter>
      </Card>
    </div>
  )
}
