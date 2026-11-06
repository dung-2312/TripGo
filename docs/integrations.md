# External integrations: what exists and what each one still needs

Status terms: **verified with real service**, **verified in sandbox**, **implemented, not
externally verified**, **not implemented**.

## Payment: verified in sandbox only

No real gateway is connected, and no real money has moved. No provider, keys or endpoints were
available, so none were invented.

- `IPaymentProvider` is the contract a gateway implements: initiate, verify a callback, refund.
- `SandboxPaymentProvider` is an in-process stand-in. It signs callbacks with HMAC-SHA256 and can
  only settle bookings flagged as sandbox. `Payments:Sandbox:Enabled=false` removes it.
- `PaymentGatewayService` owns the flow. The amount always comes from the stored booking.
- `POST /api/payments/callback/{provider}` is the only way a gateway payment becomes paid. A
  browser redirect cannot confirm anything.

| Situation | Result |
|---|---|
| Bad or missing signature | Rejected, recorded as `InvalidSignature` |
| Same event delivered again | `Duplicate`, nothing changes |
| Amount or currency differs | `AmountMismatch` / `CurrencyMismatch`, not confirmed, flagged |
| Money arrives after expiry or cancellation | `LatePayment`, not confirmed, flagged for refund |
| Payment window ends without an answer | Transaction failed, booking expired, seats released |

Flagged events appear under Admin > Giám sát > Đối soát thanh toán.

`PaymentService.ProcessPaymentAsync` is the older simulated flow used by the sandbox checkout
buttons. It refuses non-sandbox bookings and is unavailable when the sandbox provider is off.
The customer checkout has not been moved to the gateway flow; connecting a real gateway requires
that change (redirect the customer, then wait for the callback).

**To connect a real gateway:** a merchant contract and the provider's documentation; an
`IPaymentProvider` built to their signature rules; merchant ID and callback secret in a secret
store; a public HTTPS callback URL; provider sandbox testing, including their refund flow.

## SMTP: implemented, not externally verified

No SMTP credentials or test recipient were available, so no real email was sent. Tests use a fake
sender. Settings are in [email-configuration.md](email-configuration.md).

`Sent` in the outbox means the SMTP server accepted the message, not that it reached an inbox.
A crash between acceptance and our record can cause one duplicate; a stable `Message-ID` helps
receiving systems recognize it but cannot rule it out.

**To verify:** an authorized SMTP account, a verified sender domain, and an explicit test recipient.

## QR ticket verification: verified in sandbox only

Commercial ticket issuance is not authorized, so every code is labelled SANDBOX on screen and in
the verification result. Tokens are 256-bit random values holding no passenger data. Only a hash
is searchable; the owner's copy is encrypted with Data Protection.

Boarding is one-time, enforced by an atomic update. Attempts are recorded and limited to 30 per
minute per staff account. Only Admin and Operator accounts can verify, and an Operator only for
their own operator (`Users.OperatorId`, which has no admin screen yet).

**To go commercial:** operator authorization for the ticket as a boarding document, a scanning
device workflow, and a decision on revocation and refund rules.

## Operator and transport data: not implemented

`ITransportDataFeed` is the boundary, with no implementation. Operators, routes and trips carry
`DataSource` and `SourceReference`. An operator becomes verified only when an admin records
evidence, and that action is audited.

Only sandbox trips can be booked. Reference schedules are shown but never sold.

**To integrate:** an operator's authorization and API or feed documentation, plus an approved data
owner to review the claims in [data/verified-transport-data.md](data/verified-transport-data.md).
