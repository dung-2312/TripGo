# Transactional Email Configuration

TripGo supports password-reset email and sandbox booking-confirmation email
through `SmtpEmailSender`. No provider credentials are stored in the
repository. When any required setting is absent, password-reset requests
return the same generic response without sending a message or retaining a
usable reset token.

Configure these values through deployment secrets or environment variables,
not checked-in settings files:

| Key | Purpose |
|---|---|
| `Email__Smtp__Host` | SMTP server hostname |
| `Email__Smtp__Port` | SMTP submission port |
| `Email__Smtp__Username` | SMTP account |
| `Email__Smtp__Password` | SMTP credential |
| `Email__Smtp__EnableSsl` | Whether SMTP transport TLS is enabled |
| `Email__FromAddress` | Verified sender address |
| `Email__FromName` | Sender display name |
| `Email__BaseUrl` | Public application origin used to construct reset links |

Use the provider's authenticated submission endpoint and require TLS. The
application URL must be the canonical public URL; reverse-proxy and forwarded
header configuration must not be used to construct this link. Verify the
provider's sender/domain setup and deliverability before enabling the flow in
production.

Reset links expire after 15 minutes and are single-use. The current token
store is process-local, so outstanding links stop working after an application
restart and are not shared across multiple application instances. Production
deployment with multiple instances requires a shared, persistent, hashed token
store with atomic single-use consumption before it can provide reliable
cross-instance reset behavior.

## Sandbox booking-confirmation outbox

After a successful sandbox payment confirmation, the application writes a
booking-confirmation message to `BookingConfirmationEmailOutbox` in the same
database transaction. The background worker sends queued messages only when
the connected database is exactly `TripgoDb_IsolatedTest` and migration
`20261105080107_BookingConfirmationEmailOutbox` is applied. It is intentionally
disabled for `TripgoDb` and all other database names. Do not change this gate
or apply this migration to the main database without separate authorization.

The worker polls every 20 seconds, processes up to 10 messages per batch, and
retries failures after 1, 5, 15, 60, and 180 minutes. After five attempts a
message is marked `Failed`, and an administrator can requeue it under
Admin > Giám sát. `Email__Smtp__TimeoutSeconds` (5-100, default 60) limits one
send attempt, including connecting. The outbox also carries booking
acknowledgement, payment failure and cancellation messages.
Delivery is at-least-once, so a process failure after SMTP accepts a message
but before the outbox records it as sent may result in a duplicate email.

These emails describe sandbox booking data only. They are not commercial
tickets, real payment receipts, real seat confirmations, or authorization to
board. SMTP configuration does not make sandbox bookings or payments real.
