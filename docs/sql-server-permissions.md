# SQL Server Permission Requirements

This document describes the database-side controls required for TripGo. It is
not an executable migration or permission script. A SQL Server administrator
must review and apply the controls in the appropriate environment.

## Required boundary

`TripgoDb` is the main/reference database and must remain read-only for the
TripGo runtime identity. The runtime identity must not be a member of
`db_owner`, `db_ddladmin`, `db_datawriter`, or any custom role that grants
write/DDL access in `TripgoDb`.

The application currently models reference and transactional tables in the
same EF Core context. Before enabling production bookings, provision a
separate transactional database and an explicitly scoped runtime identity, or
design an approved schema-level permission boundary. Do not grant the
application broad write permission to `TripgoDb` to make booking features work.

## Permission matrix

| Principal | Database | Minimum access |
|---|---|---|
| TripGo runtime reference reader | `TripgoDb` | `SELECT` on specifically approved reference objects only |
| TripGo development/test runtime | `TripgoDb_IsolatedTest` | Required data read/write permissions for the isolated application; no access to `TripgoDb` |
| Migration operator | One explicitly selected target | Time-limited DDL/data permissions only while applying a reviewed migration; never used by the running application |
| Human DBA | As operationally required | Admin access controlled and audited outside the application |

Prefer separate SQL Server logins/identities and connection strings for these
principals. A connection string naming the intended database is not an
authorization boundary; SQL Server permissions must enforce it.

## Deployment checks

Before rollout, the DBA should:

1. Create a runtime identity with no inherited broad write or DDL grants on
   `TripgoDb`.
2. Grant read access only to the reference tables/views the runtime needs.
3. Give development/test writes only to `TripgoDb_IsolatedTest`, using a
   separate identity and environment-specific secret.
4. Keep migration credentials out of runtime configuration and user-facing
   logs.
5. Verify effective permissions using SQL Server catalog/security tooling and
   a controlled negative write-permission test approved for that environment.
6. Monitor grants and role membership for drift.

The EF `AppDbContext` also rejects tracked `SaveChanges` writes when its
configured database name is `TripgoDb`. This is a defense-in-depth guard only;
it does not replace SQL Server permissions and cannot prevent writes made
outside EF `SaveChanges` (for example, direct SQL or future set-based EF
commands).

## Current application constraints

- Development startup checks require `TripgoDb_IsolatedTest`.
- Automatic migrations are disabled by default and are permitted only for the
  approved development SQL Server and isolated database.
- Integration tests are configured for `TripgoDb_IsolatedTest`.
- No production permission changes or database writes were performed during
  this review.
