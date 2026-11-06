# External integrations: what exists and what each one still needs

Status terms: **verified with real service**, **verified in sandbox**, **implemented, not externally verified**, **architecture prepared / not integrated**, **not implemented**.

## 1. Summary of Integration Status

| Subsystem | Component | Status | Description |
|---|---|---|---|
| Payment | `SandboxPaymentProvider` | **verified in sandbox** | Fully operational demo payment flow with signed HMAC-SHA256 callbacks, success/failure test scenarios, and instant ticket generation. |
| Payment | `VnPayPaymentProvider` | **architecture prepared / not integrated** | Provider skeleton and configuration abstraction ready. Returns unavailable; rejects transactions safely without fake URLs or mock approvals. |
| Payment | `MomoPaymentProvider` | **architecture prepared / not integrated** | Provider skeleton and configuration abstraction ready. Returns unavailable; rejects transactions safely without fake charges or mock approvals. |
| Payment | Bank Transfer (VietQR) | **not implemented / disabled** | Marked "Chưa khả dụng" in UI; no fake QR code or automated confirmation without verified banking API. |
| E-Ticket | Ticket Credential & QR | **verified in sandbox** | Unique ticket code (`TKT-...`), 256-bit hashed token, single-use boarding check, rate-limited verification, and live QR code rendering. |
| Email | Outbox Background Worker | **verified in sandbox** | Transactional outbox pattern, batch processing (`UPDLOCK, READPAST`), exponential backoff retry (1m-3h), and dead-letter queueing. |
| Email | SmtpEmailSender & Templates | **implemented, not externally verified** | Responsive HTML email rendering with TripGo branding, itemized booking details, embedded base64 QR, and boarding instructions. Requires real SMTP credentials. |

---

## 2. Payment Architecture & Orchestration

TripGo adopts Clean Architecture with provider-independent payment orchestration:

```
[Customer / Web UI]
       │
       ▼ (Selects Payment Method: Sandbox Demo / VNPay / MoMo / Bank Transfer)
[IPaymentGatewayService / PaymentGatewayService]
       │
       ├──► ResolveProvider(name)
       │       ├──► SandboxPaymentProvider (Active Demo - signs callback server-side)
       │       ├──► VnPayPaymentProvider (Disabled / Architecture prepared)
       │       └──► MomoPaymentProvider (Disabled / Architecture prepared)
       │
       ▼ (Idempotency & Session Verification)
[IPaymentGatewayRepository / PaymentGatewayRepository]
       │
       ├──► Check payable booking, owner identity, and booking expiry
       ├──► Check / reuse open PaymentTransaction (idempotency key: TransactionCode)
       └──► ApplyVerifiedCallbackAsync:
               ├── Verify signature, amount, currency, and provider reference
               ├── Transition: PendingPayment -> Confirmed & Paid
               ├── Finalize seat inventory (TripSeat -> Booked)
               └── Enqueue BookingConfirmationEmailOutbox (in same DB transaction)
```

### Safety & Idempotency Rules
1. **No Client-Side Confirmation:** Frontend requests payment simulation or redirect; only verified callbacks signed by the gateway can transition a booking to `Paid`.
2. **Transaction Idempotency:** Each payment attempt reuses the open `PaymentTransaction.TransactionCode` as the idempotency key, preventing double-charging.
3. **Sandbox Isolation:** Sandbox providers strictly reject non-sandbox bookings, and production providers cannot settle sandbox test data.
4. **Unavailable Provider Protection:** Calling `InitiatePaymentAsync` or `HandleCallbackAsync` on unintegrated providers (`VNPAY`, `MOMO`) immediately returns `ProviderUnavailable` and will never confirm bookings.

---

## 3. VNPay Integration Prerequisites

To connect the official VNPay Payment Gateway in the future:
1. **Commercial Contract:** Formal merchant agreement with VNPay / VNPAY-QR.
2. **Merchant Credentials:**
   - `Payments:VnPay:TmnCode`: Terminal ID supplied by VNPay.
   - `Payments:VnPay:HashSecret`: Secret key used for HMAC-SHA512 checksum.
   - `Payments:VnPay:BaseUrl`: Gateway redirect URL (Sandbox: `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html`).
3. **Public IPN / Return Endpoints:**
   - Public HTTPS callback URL for IPN webhook: `POST /api/payments/callback/VNPAY`.
   - Customer return redirect URL: `GET /thanh-toan/ket-qua`.
4. **Provider Implementation Tasks:**
   - Construct standard VNPay query parameters (`vnp_Version`, `vnp_Command`, `vnp_TmnCode`, `vnp_Amount`, `vnp_CreateDate`, `vnp_CurrCode`, `vnp_IpAddr`, `vnp_Locale`, `vnp_OrderInfo`, `vnp_OrderType`, `vnp_ReturnUrl`, `vnp_TxnRef`).
   - Generate `vnp_SecureHash` via HMAC-SHA512.
   - Validate checksum in `VnPayPaymentProvider.VerifyCallback` using `HashSecret`.
   - Activate via `Payments:VnPay:Enabled=true`.

---

## 4. MoMo Integration Prerequisites

To connect the official MoMo Payment Gateway:
1. **Merchant Contract:** Business agreement with MoMo (M4B - MoMo for Business).
2. **Merchant Credentials:**
   - `Payments:Momo:PartnerCode`: MoMo Partner Identifier.
   - `Payments:Momo:AccessKey`: API Access Key.
   - `Payments:Momo:SecretKey`: HMAC-SHA256 signature secret.
   - `Payments:Momo:Endpoint`: API endpoint (Sandbox: `https://test-payment.momo.vn/v2/gateway/api/create`).
3. **Public Webhook Endpoint:**
   - Public HTTPS IPN webhook: `POST /api/payments/callback/MOMO`.
4. **Provider Implementation Tasks:**
   - Build MoMo All-in-One API payload (`captureWallet` or `payWithMethod`).
   - Compute signature using HMAC-SHA256 over raw signature string.
   - Verify IPN signatures and response codes (`resultCode == 0`).
   - Activate via `Payments:Momo:Enabled=true`.

---

## 5. Bank Transfer (VietQR) Prerequisites

To automate direct bank transfer:
- VietQR / Open Banking API integration (e.g. Casso, SePay, or direct bank Open API).
- Webhook listener for incoming credit notifications.
- Reconciliation against `PaymentTransaction.TransactionCode` in transfer content.
- Automated balance verification before marking order paid.

---

## 6. E-Ticket & Boarding Pass Completion

1. **Unique Ticket Credential:**
   - Every confirmed and paid booking generates a unique ticket credential with code format `TKT-{id:D6}`.
   - 256-bit cryptographically random token prefix `tgt_...` stored as a SHA-256 hash.
   - The token contains no PII (Personally Identifiable Information).
2. **Live QR Generation:**
   - Generated client-side using `QRCoder` as SVG or PNG base64.
   - Clearly stamped `SANDBOX` in test environments.
3. **Boarding & Verification Security:**
   - Single-use boarding enforced atomically via SQL row updates (`TryMarkBoardedAsync`).
   - Rate limiting: Max 30 scans per minute per staff account.
   - Replay protection: Used tickets report `AlreadyBoarded` with timestamp.
   - Expired or cancelled bookings immediately invalidate ticket verification (`BookingNotConfirmed` / `Expired`).

---

## 7. Email Outbox & E-Ticket Workflow

1. Booking confirmed & paid in `PaymentGatewayRepository.MarkPaid`.
2. Outbox record created in `BookingConfirmationEmailOutbox` within the same database transaction.
3. `BookingConfirmationEmailWorker` claims batches using `UPDLOCK, READPAST, ROWLOCK`.
4. `SmtpEmailSender` generates responsive HTML email with:
   - TripGo logo and header.
   - Booking Code & Ticket Code (`TKT-...`).
   - Passenger name, phone, operator name, route, departure date & time.
   - Seat numbers and total price.
   - Embedded QR code generated via `QRCoder.PngByteQRCode` and attached inline via MIME `multipart/related` CID (`cid:ticket-qr`) for universal email client support (Gmail, Outlook, Apple Mail).
   - Boarding instructions (present ticket 30 minutes prior to departure).
   - Sandbox disclaimer banner.
5. SMTP delivery:
   - If credentials missing: Marks `Failed` with reason `SMTP delivery is not configured` (never falsely marks `Sent`).
   - If SMTP sends: Updates status to `Sent` with timestamp.
   - If network/SMTP fails: Retries exponentially (1m, 5m, 15m, 1h, 3h).
   - After 5 failed attempts: Moved to Dead Letter (`Failed`) for administrative review.
