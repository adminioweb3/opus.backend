# Citationly Admin Dashboard

Admin panel for managing users and organization data in Citationly.

## Setup

1. Install dependencies:

```bash
npm install
```

2. Configure environment variables in `.env.local`:

```bash
VITE_API_BASE=http://localhost:8088
```

## Development

Start the dev server:

```bash
npm run dev
```

The admin panel will run on `http://localhost:5173`

## Login

Sign in with a real admin account. The backend verifies the credentials and returns a short-lived bearer token.

## Features

- User Management: View all users across organizations
- User Deletion: Delete users and cascade-delete all associated data
- Organization Data: See associated organization information for each user

## Security

The admin panel no longer ships a secret in the client bundle. The backend issues and validates admin sessions server-side.

## Building

```bash
npm run build
```

Output will be in the `dist/` directory.
