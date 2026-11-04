# Current architecture and FinanceHub gap map

## Runtime architecture

```mermaid
flowchart LR
    U[User browser / PWA] -->|HTTPS| V[Angular 17 on Vercel]
    V -->|Supabase Auth| A[Supabase Auth]
    V -->|Bearer JWT + JSON or multipart| API[ASP.NET Core 8 API on Render]
    API -->|User JWT + anon key| REST[Supabase PostgREST]
    V -->|User JWT| S[Private Supabase Storage]
    REST --> DB[(PostgreSQL)]
    S --> B[(Private receipts bucket)]
    DB --> C[pg_cron recurring expense job]
    GH[GitHub Actions] -->|build + tests| CI[Disposable Supabase stack]
    GH -->|approved workflow| PROD[Hosted migration deployment]
```

## Existing application boundaries

- **Angular PWA:** authentication, onboarding, dashboard, expenses, budgets, finance tools, family management, notifications and the data-source catalogue.
- **ASP.NET Core API:** validates Supabase JWTs, applies application validation, and calls PostgREST with the same user token so database RLS remains the final authorization boundary.
- **Supabase Auth:** email confirmation, password recovery and JWT issuance.
- **PostgreSQL:** family ledger, categories, expenses, budgets, savings, EMIs, notifications, recurring-expense jobs and FinanceHub aggregation tables.
- **Supabase Storage:** private, user-prefixed receipt objects exposed only through short-lived signed URLs.
- **Delivery:** Angular preview/production deployments through Vercel, API container on Render, and guarded Supabase migration deployment through GitHub Actions.

## Current FinanceHub foundation

The database already contains:

- `financial_accounts`
- `provider_connections`
- `import_runs`
- `raw_transactions`
- `financehub_transactions`
- `transaction_matches`
- `categorization_rules`

The source catalogue exposes bank, card, UPI and cash capability descriptions. Cash uses the existing expense flow. Bank, card and UPI connection attempts correctly return HTTP 501 because no regulated provider is configured.

## Current transaction paths

### Manual expense

```mermaid
sequenceDiagram
    participant UI as Angular
    participant API as Expenses API
    participant DB as PostgreSQL/RLS
    participant ST as Private Storage
    UI->>ST: Upload optional receipt
    UI->>API: Create/update expense with private object path
    API->>DB: Validate family/category and write expense
    UI->>ST: Remove replaced/deleted object
```

### Statement import

```mermaid
sequenceDiagram
    participant UI as Angular
    participant API as FinanceHub Imports API
    participant P as CSV/PDF parser
    participant DB as Atomic import RPC
    UI->>API: Upload statement for preview
    API->>P: Validate and parse in memory
    P-->>UI: Rows, errors, duplicate-independent checksum
    UI->>API: Re-upload and confirm import
    API->>P: Parse deterministically
    API->>DB: Commit normalized rows with idempotency key
    DB-->>UI: Imported, duplicate and review counts
```

## Gap decision

| Capability | Current state | Decision for this phase |
|---|---|---|
| Automatic bank sync | Blocked by FIU/AA onboarding | Keep disabled; add a provider adapter contract only |
| Automatic card sync | Blocked by provider/issuer coverage | Keep disabled; support card statements |
| Direct UPI history | No generic consumer API | Detect UPI from bank/card narration and references |
| CSV import | Generic import implemented | Add initial SBI, HDFC and ICICI header profiles plus fixtures |
| PDF import | Text-PDF import implemented | Add local scanned-PDF OCR with mandatory review |
| Raw ingestion integrity | Client policies are too broad | Make raw/canonical ingestion service-owned through a guarded RPC |
| Deduplication | Schema supports fingerprints | Enforce deterministic fingerprints and import idempotency |
| User review | Preview and correction implemented | Add reconciliation totals and explicit variance confirmation |
| Background sync | No provider | Defer until provider onboarding |
| Provider credentials/webhooks | No provider | Define adapter boundary; do not add fake credentials or webhooks |

## Delivery slice

This phase implements user-initiated CSV and PDF statement imports, institution header profiles, local OCR, preview, reconciliation, validation, UPI rail detection, deterministic deduplication, immutable raw records, canonical transaction writes, optional time-limited original retention and RLS tests. Automatic sync remains unavailable until a regulated provider relationship exists.
