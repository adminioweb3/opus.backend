# Admin Dashboard Setup Guide

## Prerequisites

- Node.js 18+ installed
- Backend API running on `http://localhost:8088`
- Backend configured with `Admin__Username`, `Admin__Password`, and `Admin__JwtSigningKey` in environment variables or an untracked `.env`

## Quick Start

### 1. Backend Configuration

Set the backend admin auth values in an untracked environment file or your deployment secrets:

- `Admin__Username`
- `Admin__Password`
- `Admin__JwtSigningKey`
- optional: `Admin__JwtIssuer`
- optional: `Admin__JwtAudience`

### 2. Frontend Setup

#### Install dependencies:

```bash
cd admin
npm install
```

#### Configure environment variables:

The `.env.local` file is pre-configured with:

```bash
VITE_API_BASE=http://localhost:8088
```

Change `VITE_API_BASE` if your backend runs on a different host or port.

#### Start development server:

```bash
npm run dev
```

The admin panel will be available at: `http://localhost:5173`

### 3. Login

Use the backend-configured admin account credentials.

## Features

### User Management

- View all users: See a complete list of all users and their associated organizations
- Delete user: Remove a user and cascade-delete all their organization’s data

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

```bash
POST /api/Admin/login
Body: { "username": "...", "password": "..." }
Response: { accessToken, expiresAt, role }

GET /api/Admin/users
Headers: Authorization: Bearer <accessToken>
Response: Array of AdminUserRow objects

DELETE /api/Admin/users/{userId}
Headers: Authorization: Bearer <accessToken>
Response: Success message
```

## Troubleshooting

### "Failed to fetch users"

- Check that the backend is running on the configured API_BASE
- Verify the admin access token is present and the backend admin env vars are set
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

- No admin secret is bundled into the client
- Keep the backend admin credentials and JWT signing key in deployment secrets or an untracked `.env`
- Only deploy to HTTPS endpoints in production
- Consider using environment variables instead of hardcoded secrets in CI/CD
