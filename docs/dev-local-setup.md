# Local development on Windows (Aspire, installed SQL Server)

This branch (`dev/local-setup`) runs Starfall for development with .NET Aspire on Windows, against an installed SQL
Server, with the real Aurora Legacy and 5etools content. It is not the self-hosting install: for playing with a group,
use `deploy/setup.sh` ([installation.md](installation.md)).

## Prerequisites

- .NET 10 SDK, Node.js 22.19+ (runs the Compendium builder's TypeScript directly), Git.
- The Aspire CLI: `dotnet tool install --global Aspire.Cli --version 13.4.*` (match `Aspire.AppHost.Sdk` in
  `src/aspire/Starlights.AppHost/Starlights.AppHost.csproj`).
- A trusted HTTPS dev certificate: `dotnet dev-certs https --check --trust` (or `--trust` to create one).
- SQL Server 2019+ reachable with Windows authentication and the right to create a database. Without it the AppHost
  falls back to SQL Server in Docker (Docker Desktop must then be running).
- The frontend packages: `cd src/frontend && npm ci`.

## One-time configuration (user secrets, never in git)

From `src/aspire/Starlights.AppHost`:

```powershell
# your SQL Server; the database is created by the migration workers on the first run
dotnet user-secrets set "ConnectionStrings:starlights-db" 'Server=localhost\SQL2022;Database=StarfallDev;Trusted_Connection=True;TrustServerCertificate=True'

# the admin key (content administration API, X-Admin-Key) and the master password (the app's key button)
dotnet user-secrets set "Parameters:admin-key" "<random string>"
dotnet user-secrets set "Parameters:master-password" "<a password you will type>"
```

Leave out `ConnectionStrings:starlights-db` to use the SQL Server container instead. A missing parameter is asked for in
the dashboard.

## Run

```powershell
aspire run          # from the repository root; https profile, opens the dashboard link in the console
```

The AppHost (`src/aspire/Starlights.AppHost/AppHost.cs`) runs the two migration workers, then the API with the
development content settings (`Content:SelfManaged=true`, `Elements:SampleContent=false`, and the folders below), then
the builder app (`app-character-builder`, the whole Starfall site). Its URL is in the dashboard. The landing-page,
content-manager and showcase apps are early scaffolds and are not started; to run one by hand, build the shared
packages first (`npm run build:packages` in `src/frontend`), since they import `ui-framework` from its `dist/`.

| What | Where |
|---|---|
| Downloaded content (Aurora Legacy, 5etools), homebrew, portraits | `.devdata/` in the repository root (git-ignored) |
| Compendium of Lore | `.devdata/lore`, served at `/lore-data` by the builder's Vite dev server (`LORE_DIR`, `vite.config.ts`) |

The Compendium is kept out of `public/` on purpose: Vite watches that folder, and on Windows its open handles stop
the Compendium builder from renaming its finished folder into place.

## Content (first run, and to update it)

1. Open the builder app, press the **key button** and enter the master password.
2. Open the **Content** page and start the update. The first run downloads about 200 MB from GitHub and imports it
   (20–40 minutes): base rules, Aurora Legacy, what 5etools adds, the built-in extras, then the Compendium.

Or with the API: `POST https://localhost:7246/api/admin/content-sources/update` with `X-Admin-Key` and `{"force":false}`;
`GET /api/admin/content-sources` shows the job's steps.

Don't use the dashboard's **Initialize Database** command on its own: it only creates the base rules (the content
update does that itself on an empty database).

## Starting over

Stop the AppHost, drop the database (`DROP DATABASE StarfallDev` in SSMS or `sqlcmd`) and delete `.devdata/`; the
next `aspire run` recreates the database.
