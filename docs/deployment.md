# Deployment readiness

No deployment has been performed. This describes what a deployment requires.

## Blocking decision: where production writes go

`AppDbContext` refuses every write except to `TripgoDb_IsolatedTest`, and `TripgoDb` is read-only.
**The application therefore cannot take a booking in any production database today.** This is
deliberate and must not be loosened casually. Before production, someone must decide on a
separate transactional database and change that guard deliberately, together with SQL
permissions ([sql-server-permissions.md](sql-server-permissions.md)).

## Configuration

Supply secrets through environment variables or a secret store, never checked-in files.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | Runtime database, with a least-privilege identity |
| `DataProtection__KeyRingPath`, `DataProtection__CertificateThumbprint` | Shared keys for cookies and ticket codes |
| `Payments__Sandbox__Enabled` | `false` in production (the default in `appsettings.Production.json`) |
| `Email__*` | See [email-configuration.md](email-configuration.md) |

`appsettings.json` names `TripgoDb` as its default connection. Always override it explicitly.

## Runtime

- HTTPS: HSTS and HTTPS redirection are on outside Development; cookies are `Secure` and `SameSite=Strict`.
- Health: `/health/live` (process up) and `/health/ready` (database reachable). Neither exposes details.
- Workers: the expiry and email workers run in every instance and are safe to run together, because
  outbox rows are claimed with row locks. Both stop cleanly with the host.
- Rate limits: the payment callback and ticket API allow 60 requests per minute per IP. Behind a proxy,
  configure forwarded headers first, or every client shares one limit.
- Logging: SQL command logging is reduced in `appsettings.Production.json`. Do not log cookies or tokens.

## Known gaps

- Password-reset tokens are held in process memory, so they stop working on restart and across instances.
- Login has no attempt limit or lockout.
- Admin access is local accounts; [phase3a-hardening.md](phase3a-hardening.md) recommends an identity provider with MFA.
- Backups: `TripgoDb` backup and restore need a tested procedure owned by a DBA before go-live.
  Take a full backup before any migration, and test restoring it.

## Migrations

Apply reviewed migrations through an operator identity, never at application startup.
`ApplyMigrationsOnStartup` only works in Development against `TripgoDb_IsolatedTest`.
Generate a script to review with `dotnet ef migrations script --idempotent`.
