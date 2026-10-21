# Transaction processing workflow

## State flow

1. Acquire consent or accept a user-initiated statement upload/manual cash entry.
2. Create an import_runs row with a stable idempotency key.
3. Verify provider webhook signatures or validate file type, size and parser version.
4. Persist the immutable source record in raw_transactions with a checksum.
5. Normalize into the canonical transaction shape.
6. Resolve the source account and payment rail.
7. Deduplicate using external IDs, UTR/RRN and deterministic fingerprints.
8. Link transfers and card-bill payments without double-counting spend.
9. Apply user categorization rules, then optional suggestions.
10. Write the canonical transaction and an audit event in one database transaction.
11. Update import counts/cursor and surface low-confidence items for review.
12. Reconcile later provider updates, reversals and deletions without erasing history.

## Idempotency

The database enforces uniqueness for provider/external references and raw checksums within a connection. Every webhook delivery and scheduled poll must supply or derive an idempotency key. Retrying a failed import resumes from its recorded cursor; it does not create a parallel ledger.

## Canonical amount rules

Store amount as a positive numeric value and direction as debit or credit. A separate transaction_type describes purchase, income, transfer, fee, refund, cash or adjustment. Reporting converts debit/credit into signed values at query time. This avoids inconsistent negative amounts across providers.

## Reconciliation cases

- Pending card item becomes posted: update status through a new source event and retain lineage.
- Reversal/refund: create or link a separate credit rather than mutating the original amount.
- Own-account transfer: link the debit and credit and exclude it from expense/income totals.
- Credit-card bill payment: link to the card account payment and avoid double-counting purchases.
- Duplicate candidate: keep both raw records, mark the canonical candidate for review and record the score/reasons.
- Provider correction: version material canonical changes in audit metadata.

## Failure handling

Classify failures as retryable, user_action_required or terminal. Use exponential backoff with jitter and provider rate limits. Stop retrying revoked or expired consent. Show a user-safe message while keeping redacted diagnostics and correlation IDs for operations.

## Observability

Track sync success rate, freshness lag, imported rows, duplicate rate, review rate, categorization confidence, provider latency and webhook failures. Never log access tokens, account numbers, full narrations containing sensitive data, statement contents or uploaded files.
