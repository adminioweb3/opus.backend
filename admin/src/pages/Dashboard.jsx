import { useState, useEffect } from 'react'
import { LogOut, Trash2, Users, AlertCircle } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

const API_BASE = import.meta.env.VITE_API_BASE || 'http://localhost:8088'

export default function Dashboard({ onLogout }) {
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [deleting, setDeleting] = useState(null)
  const [deleteError, setDeleteError] = useState('')
  const [deleteSuccess, setDeleteSuccess] = useState('')

  useEffect(() => {
    fetchUsers()
  }, [])

  const getAuthHeaders = () => {
    const token = localStorage.getItem('admin_token')
    return token ? { Authorization: `Bearer ${token}` } : {}
  }

  const fetchUsers = async () => {
    try {
      setLoading(true)
      setError('')
      const response = await fetch(`${API_BASE}/api/Admin/users/all`, {
        headers: getAuthHeaders(),
      })

      if (response.status === 401) {
        onLogout()
        return
      }

      if (!response.ok) throw new Error('Failed to fetch users')
      const data = await response.json()
      setUsers(Array.isArray(data) ? data : [])
    } catch (err) {
      setError(err.message || 'Failed to load users')
    } finally {
      setLoading(false)
    }
  }

  const handleDeleteUser = async (userId) => {
    if (!window.confirm('Are you sure you want to delete this user and all related data? This cannot be undone.')) {
      return
    }

    try {
      setDeleting(userId)
      setDeleteError('')
      setDeleteSuccess('')

      const response = await fetch(`${API_BASE}/api/Admin/users/${userId}`, {
        method: 'DELETE',
        headers: getAuthHeaders(),
      })

      if (response.status === 401) {
        onLogout()
        return
      }

      if (!response.ok) throw new Error('Failed to delete user')

      setDeleteSuccess('User and all related data deleted successfully')
      await new Promise(r => setTimeout(r, 1500))
      fetchUsers()
      setDeleteSuccess('')
    } catch (err) {
      setDeleteError(err.message || 'Failed to delete user')
    } finally {
      setDeleting(null)
    }
  }

  return (
    <div className="min-h-screen bg-muted/30">
      <header className="bg-card shadow-sm border-b">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-4 flex justify-between items-center">
          <div className="flex items-center gap-2">
            <Users className="w-6 h-6 text-primary" />
            <h1 className="text-2xl font-bold">Admin Panel</h1>
          </div>
          <Button variant="destructive" onClick={onLogout}>
            <LogOut className="w-4 h-4" />
            Logout
          </Button>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-4">
        {deleteSuccess && (
          <Alert variant="success">
            <AlertDescription>{deleteSuccess}</AlertDescription>
          </Alert>
        )}

        {error && (
          <Alert variant="destructive">
            <AlertCircle className="w-4 h-4" />
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}

        {deleteError && (
          <Alert variant="destructive">
            <AlertCircle className="w-4 h-4" />
            <AlertDescription>{deleteError}</AlertDescription>
          </Alert>
        )}

        <Card className="py-0 gap-0 overflow-hidden">
          <CardHeader className="border-b py-4">
            <CardTitle>Users ({users.length})</CardTitle>
            <CardDescription>Manage all users in the system</CardDescription>
          </CardHeader>

          <CardContent className="px-0">
            {loading ? (
              <div className="px-6 py-12 text-center">
                <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-primary"></div>
                <p className="mt-4 text-muted-foreground">Loading users...</p>
              </div>
            ) : users.length === 0 ? (
              <div className="px-6 py-12 text-center">
                <Users className="w-12 h-12 text-muted-foreground mx-auto mb-4" />
                <p className="text-muted-foreground">No users found</p>
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-6">Email</TableHead>
                    <TableHead>Display Name</TableHead>
                    <TableHead>Organization</TableHead>
                    <TableHead>Role</TableHead>
                    <TableHead>Source</TableHead>
                    <TableHead>Created</TableHead>
                    <TableHead className="text-right pr-6">Action</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {users.map((user) => {
                    const isFirebaseOnly = user.organizationId === '00000000-0000-0000-0000-000000000000' || user.organizationId === '00000000000000000000000000000000'
                    return (
                      <TableRow key={user.id}>
                        <TableCell className="pl-6 font-medium">
                          {user.email}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {user.displayName}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {user.organizationName}
                        </TableCell>
                        <TableCell>
                          <Badge variant="secondary">{user.role}</Badge>
                        </TableCell>
                        <TableCell>
                          <Badge variant={isFirebaseOnly ? 'warning' : 'success'}>
                            {isFirebaseOnly ? 'Firebase' : 'Database'}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {new Date(user.userCreatedAt).toLocaleDateString()}
                        </TableCell>
                        <TableCell className="text-right pr-6">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleDeleteUser(user.id)}
                            disabled={deleting === user.id || isFirebaseOnly}
                            title={isFirebaseOnly ? 'Firebase-only users cannot be deleted from admin panel' : 'Delete user and all related data'}
                            className="text-destructive hover:text-destructive hover:bg-destructive/10"
                          >
                            <Trash2 className="w-4 h-4" />
                            {deleting === user.id ? 'Deleting...' : 'Delete'}
                          </Button>
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        <Alert>
          <AlertDescription>
            <strong>Note:</strong> Deleting a user will remove them and all associated data including organizations, websites, competitors, reports, and prompts. When the user re-onboards, they will start with a fresh account.
          </AlertDescription>
        </Alert>
      </main>
    </div>
  )
}
