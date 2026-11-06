# Deployment and environment readiness

No production deployment has been performed. This document distinguishes local development,
automated testing, sandbox demonstrations, and production requirements.

## Database target and write safety

`AppDbContext` and the runtime guard currently permit application writes only to
`TripgoDb_IsolatedTest`. `TripgoDb` is not an approved write target and must remain read-only.
The application therefore cannot accept production bookings or run write-enabled production
workers today. Do not change the guard to make a deployment work. Production requires a separately
approved database, least-privilege SQL identity, reviewed guard changes, and a tested backup/restore
procedure. No production database has been approved.

Always set `ConnectionStrings__DefaultConnection` explicitly and verify the effective connection
and connected database before any operation that can write. Migrations and test seeding must never
target `TripgoDb`.

## Environment profiles

| Environment | Database and writes | Payments | Background workers | Email |
|---|---|---|---|---|
| Development | The runtime guard requires `TripgoDb_IsolatedTest`. | Sandbox provider is explicitly enabled in `appsettings.Development.json`. | Enabled only when `Workers:*:Enabled=true`, target is exactly `TripgoDb_IsolatedTest`, and the connected database matches. | Configure a test sender or SMTP explicitly; absent valid SMTP credentials, delivery is not verified. |
| Automated test | Integration tests use the isolated database and test-specific sender/provider. | Signed sandbox callbacks and test providers; not commercial settlement. | Worker policy tests verify fail-closed configuration; database-backed worker tests use only the isolated database. | Mock/test sender only; no SMTP success is inferred. |
| Sandbox demonstration | Must use the isolated database and the explicitly enabled sandbox provider. | Simulated/signed callbacks demonstrate application flow only. | Same isolated-database target and explicit enablement as Development. | SMTP requires separately supplied credentials and an end-to-end delivery check. |
| Production | **Blocked:** no approved writable production database; current write guard rejects all targets except the isolated test database. | Sandbox is disabled by default; no commercial gateway is integrated. | Disabled by default and always refused outside Development. No production worker target is approved. | Not verified without production SMTP credentials and a delivery test. |

## Configuration

Supply secrets through environment variables or an approved secret store, never checked-in
configuration.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | Runtime SQL Server connection; verify its database name before writes. |
| `DataProtection__KeyRingPath`, `DataProtection__CertificateThumbprint` | Persistent, protected keys for cookies and protected ticket tokens in multi-host deployments. |
| `Payments__Sandbox__Enabled` | Sandbox provider switch. It is false when not explicitly enabled; keep it false in Production. |
| `Auth__Security__*` | Login lockout, password-reset request/window, and reset-token lifetime limits. |
| `Auth__TrustedProxies__0` | Exact IP address of a trusted reverse proxy; forwarded headers are ignored unless the sender matches a configured proxy. |
| `Workers__BookingExpiration__Enabled` | Explicit switch for booking expiration worker. |
| `Workers__BookingExpiration__TargetDatabase` | Must exactly be `TripgoDb_IsolatedTest` for the worker to run. |
| `Workers__BookingConfirmationEmail__Enabled` | Explicit switch for email outbox worker. |
| `Workers__BookingConfirmationEmail__TargetDatabase` | Must exactly be `TripgoDb_IsolatedTest` for the worker to run. |
| `Email__*` | Email sender configuration; see [email-configuration.md](email-configuration.md). |

`appsettings.json` points at `TripgoDb` as a default connection but disables both workers and the
sandbox provider. `appsettings.Development.json` points to the isolated database and explicitly
enables the sandbox and isolated-database workers. Override these values deliberately per
environment. Production worker settings must remain disabled until a separately approved production
write target and guard policy exist; the current worker policy also refuses to run outside
Development.

## Password reset and authentication throttling

Password-reset tokens are persisted in the database as hashes, expire according to
`Auth:Security:PasswordResetTokenLifetimeMinutes`, and are consumed once. Issuance invalidates
previous active reset credentials for the account; a failed email send invalidates the newly issued
token. The actual reset email still requires a configured sender.

Login lockout and password-reset throttling use a bounded, TTL-cleaned **process-local in-memory**
limiter. Account, token, and client-address keys are HMACed in memory; passwords and raw reset
tokens are never stored in limiter entries. It applies request windows by login identifier and,
when a server-side connection address is available, by client IP. Password-reset requests and
verification attempts have corresponding identifier/token and IP windows. Capacity is bounded:
expired entries are cleaned, active entries are never evicted, and new keys are denied when full.
Configure limits with `Auth:Security:*`; `MaximumRateLimitEntries` controls the shared entry cap.

The limiter is concurrency-safe within one process, but counters are lost on restart and are not
shared across instances. IP limits can only be applied when a server-side client address is
available; interactive Blazor circuit events may not have an `HttpContext`, in which case the
identifier/token limits still apply. Do not treat this as complete IP-based brute-force protection.
If deployed behind a proxy, set `Auth:TrustedProxies` to the exact proxy IP addresses; forwarded
headers from other sources are ignored. Startup logs warn when no trusted proxy is configured.
Before multi-instance production, configure and test a shared distributed limiter and an
edge/identity-provider control that covers interactive authentication requests.

## Payments, tickets, and transport data

- Only the opt-in sandbox provider is implemented. Its signed callbacks test application behavior;
  sandbox bookings and QR codes are explicitly test-only and are not commercially payable or valid
  for real travel.
- No commercial payment gateway, production credentials, settlement, or refund integration has
  been verified. Production payment readiness is blocked.
- Ticket QR tokens are random, stored as hashes plus protected display material, checked against
  booking/payment status, expiration, revocation, and one-time check-in state. The QR does not
  contain passenger personal data.
- No licensed or otherwise verified transport feed is configured. Do not present unverified routes,
  schedules, operators, prices, phone numbers, or statistics as real.

## Background worker safety

Workers are not enabled for arbitrary databases. Each worker requires an explicit enable flag,
an explicit `TripgoDb_IsolatedTest` target, a Development environment, and a matching connected
database. They log whether they are active or disabled and why. The `AppDbContext` write guard is
unchanged. Production workers remain blocked; they must not be enabled merely by changing an
environment variable.

The outbox uses database leasing, bounded retries, and dead-letter status. Multiple worker
instances in a permitted isolated test deployment coordinate through SQL row claims. The email
sender's actual SMTP delivery is not proven until valid credentials are configured and delivery is
verified; automated tests use a test sender.

## Operational checks

- HTTPS redirection and secure cookies are enabled outside Development.
- `/health/live` reports process liveness and `/health/ready` reports database reachability.
- Payment callback and ticket API rate limits are per IP. Configure trusted forwarded headers
  before placing the app behind a proxy; otherwise clients may share an IP-based limit.
- Authentication limiter status is process-local as described above.
- Authentication IP limiting is best-effort for Interactive Server events because the server-side client address may not be available on an established circuit. Keep an edge/identity-provider control in place; do not rely on the in-memory limiter as the sole production defense.
- Do not log passwords, reset tokens, cookies, QR tokens, or provider secrets.
- Production startup must be reviewed with the approved database, data protection key sharing,
  identity/MFA, payment, SMTP, transport-data licensing, monitoring, and recovery controls in place.

## Migrations

Do not apply migrations at application startup in Production. Apply reviewed migration scripts only
to the explicitly approved target database. `ApplyMigrationsOnStartup` is disabled by default and
the current runtime allows migration/seeding only in Development against `TripgoDb_IsolatedTest`.
Generate scripts for review with `dotnet ef migrations script --idempotent`; never point that
operation at `TripgoDb`.
