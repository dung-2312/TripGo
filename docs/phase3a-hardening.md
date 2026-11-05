# Phase 3A hardening notes

## EF Core tooling target

Design-time EF Core creation first checks
`TRIPGO_EF_CONNECTION_STRING_<ENVIRONMENT>` (using `DOTNET_ENVIRONMENT`, then
`ASPNETCORE_ENVIRONMENT`, defaulting to `Development`), then the explicitly supplied
`TRIPGO_EF_CONNECTION_STRING`. The factory has no fallback connection string and
allows only `Admin-PC\MSSQLSERVER01` (or its localhost alias) plus the project's
isolated database targets. It rejects system databases, attached-file targets, and
unrecognized application databases. It rejects `TripgoDb` unless
`TRIPGO_ALLOW_PRODUCTION_EF_TARGET=true` is explicitly set; temporary Phase 3A
rehearsal databases additionally require `TRIPGO_ALLOW_REHEARSAL_EF_TARGET=true`.
Production migrations remain a separately approved deployment step.

Runtime connection configuration continues to use
`ConnectionStrings:DefaultConnection`, including environment-variable overrides.
`ApplyMigrationsOnStartup` is explicitly `false`. If enabled for local development,
startup migration/seeding is still blocked unless the target is
`TripgoDb_IsolatedTest` on the approved SQL Server instance.

## Booking session and access control

The web host issues an ASP.NET Core protected, HttpOnly, Secure, SameSite=Strict
cookie containing a random booking-session claim. Booking application services read
the claim from the server authentication state and ignore request DTO owner values.
Holds and bookings are associated through the hold token and its stored owner.
Clearing the browser cookie intentionally loses access to that browser session's
reservations; booking codes and phone numbers alone do not authorize access.

The `/admin` pages require the `Admin` role, and `AdminService` re-checks the
account against the database on every call. Admin accounts are local for now.
Before production administration, connect an external OIDC identity
provider, require MFA, and map a provider-controlled `TripGo-Administrators` group
to the `Admin` role. Provision and revoke membership only through the identity
provider's authorized administrator workflow with approval and audit logging; do
not add a default/local administrator or accept a role from client input. Until
that provider integration and role mapping are deployed and tested, administration
remains unavailable by design.

For multiple web instances, configure `DataProtection:KeyRingPath` to a persistent,
shared, access-controlled key-ring location, configure
`DataProtection:CertificateThumbprint` for a valid certificate with its private key
installed in `LocalMachine\My` on every instance, and use the same TripGo application
name on every instance. The application encrypts persisted keys with that
certificate; protect its private key and grant access only to the application
identity. Configure these values with environment variables
`DataProtection__KeyRingPath` and `DataProtection__CertificateThumbprint`, not in
source control. Without shared keys, a cookie created on one instance may not be
read by another instance or after key loss. Do not log the cookie or booking-session
claim. When no explicit key-ring path is configured, ASP.NET Core uses its default
per-user key repository.

## Expiration and state rules

The hosted cleanup worker checks every 30 seconds. Expired holds and unpaid pending
bookings are transitioned and their seats released within serializable SQL
transactions. SQL Server retry-on-failure is enabled; transaction operations are
executed through EF's execution strategy. Cleanup is idempotent across instances.
Before touching booking tables, the worker verifies that all three Phase 3A
migrations are present. Missing migrations disable the worker and are logged at
critical level; transient SQL errors retry on the next interval, while unexpected
errors stop the worker visibly rather than repeatedly attempting cleanup.

New bookings can only be created as `PendingPayment` / `Unpaid`. Cancellation is
limited to unpaid pending bookings, is idempotent for an already-cancelled booking,
and does not release completed/paid bookings. There is no payment transition or
verified e-ticket flow yet; displayed bookings are reservations, not valid tickets.

## Production migration boundary

No migration was applied by this hardening change. Production still needs the
separately approved Phase 3A migration sequence before the application can use the
booking schema. Confirm backup restoreability, deployment connection settings, and
the shared Data Protection key ring before multi-instance production rollout.
