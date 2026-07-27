# Citationly Admin Dashboard

Admin panel for managing users and organization data in Citationly.

## Setup

1. Install dependencies:
```bash
npm install
```

2. Configure environment variables in `.env.local`:
```
VITE_API_BASE=http://localhost:8088
VITE_ADMIN_SECRET=admin-secret-key-12345
```

## Development

Start the dev server:
```bash
npm run dev
```

The admin panel will run on `http://localhost:5173`

## Login Credentials

- **Username**: `admin`
- **Password**: `pass@123`

## Features

- **User Management**: View all users across organizations
- **User Deletion**: Delete users and cascade-delete all associated data (organizations, websites, competitors, reports, prompts)
- **Organization Data**: See associated organization information for each user

## Security

The admin panel communicates with the backend using an admin secret header (`X-Admin-Secret`) configured in the backend's `appsettings.Development.json`.

Never expose this secret in version control or client-side code for production environments.

## Building

```bash
npm run build
```

Output will be in the `dist/` directory.
