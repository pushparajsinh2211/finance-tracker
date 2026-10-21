# FinanceHub phased development plan

## Phase 0 — foundation (this starter)

- Preserve the existing FamilyLedger features.
- Add canonical PostgreSQL tables, row-level security and idempotency constraints.
- Add authenticated source catalogue API and Angular source screen.
- Document trust boundaries, India-specific integration reality and processing rules.
- Add repeatable frontend/backend CI.

Exit: builds pass, migration is reviewed in a disposable Supabase project, and external sources are visibly labelled planned.

## Phase 1 — unified manual ledger

- Extend cash entry to the canonical transaction model.
- Add unified transaction search, filters, review queue and audit history.
- Implement CSV import for one bank and one card issuer with preview.
- Add deterministic deduplication and user categorization rules.
- Add export and account deletion workflows.

Exit: users can import supported statements without duplicates and reconcile totals against statements.

## Phase 2 — regulated bank sync pilot

- Select and contract an Account Aggregator/provider after legal, security and coverage review.
- Implement sandbox consent, connection lifecycle, encrypted credential references and signed webhooks.
- Add a background worker, retry policy, cursor checkpoints and freshness monitoring.
- Pilot with a small supported institution set.

Exit: consent/revocation works end to end, reconciled accuracy meets the product target, and incident/runbook exercises pass.

## Phase 3 — cards and broader coverage

- Validate card data availability with the selected provider.
- Add issuer statement parsers where automatic coverage is absent.
- Model pending/posted transactions, reversals, refunds, EMI conversion and card-bill transfer linking.
- Expand reconciliation test fixtures across institutions.

Exit: supported card statements reconcile by cycle and double-counting tests pass.

## Phase 4 — intelligence and hardening

- Add explainable categorization suggestions with user-controlled learning.
- Add anomaly and subscription insights without making financial-advice claims.
- Complete threat modelling, penetration testing, privacy review, backup/restore tests and disaster recovery.
- Add SLOs, operational dashboards and provider failover procedures.

Exit: production-readiness review is approved for the exact providers and scopes enabled.

## Testing strategy

- Unit tests for normalization, fingerprinting, transfer detection and rule precedence.
- Contract tests per provider/parser using redacted fixtures.
- Migration tests against a disposable Supabase/PostgreSQL environment.
- API integration tests for authentication, RLS and idempotency.
- Angular component tests and accessibility checks.
- End-to-end consent/import/review/revocation tests.
- Security tests for webhook replay, tenant isolation and secret leakage.
