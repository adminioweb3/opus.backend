# Admin Dashboard Setup Guide

## Prerequisites

- Node.js 18+ installed
- Backend API running on `http://localhost:8088`
- Backend configured with Admin:ResetSecret in appsettings.Development.json

## Quick Start

### 1. Backend Configuration

Ensure the backend's `appsettings.Development.json` includes:
```json
{
  "Admin": {
    "ResetSecret": "admin-secret-key-12345"
  }
}
```

### 2. Frontend Setup

#### Install dependencies:
```bash
cd admin
npm install
```

#### Configure environment variables:
The `.env.local` file is pre-configured with:
```
VITE_API_BASE=http://localhost:8088
VITE_ADMIN_SECRET=admin-secret-key-12345
```

Change these values if your backend runs on a different host/port or uses a different secret.

#### Start development server:
```bash
npm run dev
```

The admin panel will be available at: `http://localhost:5173`

### 3. Login

Use the hardcoded admin credentials:
- **Username**: `admin`
- **Password**: `pass@123`

## Features

### User Management
- **View all users**: See a complete list of all users and their associated organizations
- **Delete user**: Remove a user and cascade-delete all their organization's data:
  - User record
  - Organization record
  - All websites
  - All competitors
  - All reports
  - All profiles
  - Any other organization-related data

### Data Model

Each user row displays:
- Email
- Display Name
- Associated Organization Name
- Role
- Creation Date
- Delete Action

## API Endpoints

The admin panel communicates with the backend via:

```
GET /api/Admin/users
Headers: X-Admin-Secret: admin-secret-key-12345
Response: Array of AdminUserRow objects

DELETE /api/Admin/users/{userId}
Headers: X-Admin-Secret: admin-secret-key-12345
Response: Success message
```

## Troubleshooting

### "Failed to fetch users"
- Check that the backend is running on the configured API_BASE
- Verify the VITE_ADMIN_SECRET matches the backend's Admin:ResetSecret
- Check browser console for CORS errors (backend may need CORS configured)

### "User not found" on delete
- The user may have already been deleted
- Refresh the page to see the updated list

### Port conflicts
- Default frontend port is 5173
- To use a different port, modify the `server.port` in `vite.config.js`

## Building for Production

```bash
npm run build
```

Output will be in the `dist/` directory.

## Security Notes

- The admin secret is stored in `.env.local`, which is gitignored
- The secret should be kept secure and rotated regularly in production
- Only deploy to HTTPS endpoints in production
- Consider using environment variables instead of hardcoded secrets in `.env.local` for CI/CD
