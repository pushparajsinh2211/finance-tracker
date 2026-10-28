# FinanceHub architecture

## Scope

FinanceHub extends the existing FamilyLedger application into a personal-finance aggregation platform for Indian users. The current Angular 17 client, ASP.NET Core 8 API, Supabase authentication and PostgreSQL database remain in place. Existing family, expense, budget, EMI, savings and notification features are preserved.

FinanceHub now includes user-initiated CSV and text-based PDF statement preview/import for bank and card sources, with atomic canonical ingestion and deterministic deduplication. It does not include a production bank, card, Account Aggregator or UPI connector. Those integrations require provider onboarding, consent, compliance review and production credentials.

## System context

- Angular web/PWA: dashboard, transaction review, source management and manual cash capture.
- ASP.NET Core API: user-facing endpoints, validation, orchestration and provider adapters.
- PostgreSQL through Supabase: system of record, row-level security, audit metadata and idempotency constraints.
- Background worker, planned: consent refresh, scheduled sync, webhook processing, normalization, deduplication and categorization.
- Object storage, existing: receipts and import artifacts where retention is permitted.
- External providers, planned: RBI-regulated Account Aggregator ecosystem for deposit-account data; provider-supported card feeds; statement imports; and optional notification/email parsers with explicit consent.

## Trust boundaries

The browser receives only public configuration and the authenticated user's data. Provider secrets, client certificates, webhook secrets and refresh credentials belong in a server-side secret manager and must never be shipped to Angular. Raw payloads are encrypted at rest, access-controlled, retained for the shortest practical period and excluded from application logs.

Supabase JWT authentication remains the identity boundary. Every FinanceHub table has row-level security based on auth.uid(). API calls also require authentication, but database policy is the final isolation layer.

## Components

### Source catalogue and connection lifecycle

The source catalogue is safe to expose and describes bank, card, UPI and cash capabilities. A provider connection progresses through planned, pending_consent, active, action_required, revoked or failed. Only cash is available without an external provider in this starter.

### Ingestion

A provider adapter accepts a consent-scoped payload or an uploaded statement and stores an immutable raw_transactions record keyed by provider and external reference. An import_runs record captures timing, counts, cursor state and errors. Webhook deliveries must be verified and recorded idempotently before processing.

### Normalization

Provider-specific fields map into canonical transactions:

- signed amount plus credit/debit direction;
- occurred_at and posted_at in UTC, with the original timezone retained when supplied;
- normalized merchant, narration, reference and payment rail;
- source account and optional counterparty;
- status, confidence, category and review flag.

Money uses numeric(18,2) and an ISO currency code. India defaults to INR but is not hard-coded in the domain contract.

### Deduplication

Deduplication is deterministic first and probabilistic second. Preferred keys are provider transaction ID, UTR/RRN, statement reference and provider account. When those are absent, a fingerprint combines account, amount, direction, normalized narration and a bounded timestamp/date window. Probable matches are recorded in transaction_matches and require review instead of destructive merging.

### Categorization

User rules run before learned suggestions. Rules can match merchant text, narration, payment rail, source account and amount range. A category assignment stores its origin and confidence. User corrections are durable training signals; they do not silently rewrite historical transactions.

## API surface

The application exposes GET /api/financehub/sources, POST /api/financehub/sources/{source}/connect, POST /api/financehub/imports/preview, and POST /api/financehub/imports/commit. GET is an authenticated capability catalogue. POST deliberately returns HTTP 501 for external sources and documents the missing provider configuration. Existing expense endpoints continue to support manual cash entry.

Planned API groups:

- /api/financehub/connections for consent and connection state;
- /api/financehub/imports for additional institution parsers and import history;
- /api/financehub/transactions for unified search, review and corrections;
- /api/financehub/rules for deterministic categorization;
- /api/financehub/webhooks/{provider} for verified provider events.

## Deployment

Use separate development, staging and production Supabase projects. Apply migrations through CI with an environment approval gate. Run API and worker with distinct service identities. Production needs a managed secret store, centralized redacted logs, metrics, alerting, encrypted backups and tested restore procedures.

## Security and privacy baseline

- Explicit, purpose-limited consent with revocation and expiry.
- Least-privilege provider scopes and short-lived tokens where supported.
- TLS for every hop and encryption at rest for credentials and raw payloads.
- No account credentials, OTPs, UPI PINs or full card data collected by FinanceHub.
- PCI scope avoidance: store masked card identifiers only.
- Webhook signature verification, replay protection and idempotency keys.
- Audit events for consent, imports, exports, corrections and deletions.
- User-accessible export and deletion workflows with legally required retention exceptions.
- Threat modelling and penetration testing before any production financial-data connection.

## Repository mapping

- frontend: Angular PWA and the source catalogue screen.
- backend/FamilyLedger.Api: authenticated API contracts.
- supabase/migrations: PostgreSQL schema and RLS.
- docs: architecture, integration constraints, processing workflow and delivery plan.
