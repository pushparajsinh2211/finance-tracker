# India-specific integration plan

## Important status

All external financial-data integrations described here are planned. This repository does not contain production bank, credit-card, Account Aggregator or UPI integrations, credentials or certifications.

## Bank accounts

The preferred automatic path is the RBI Account Aggregator ecosystem, using a contracted, regulated AA or technology service provider. The user is redirected through a consent flow, selects supported Financial Information Providers, approves purpose and duration, and can revoke consent. FinanceHub stores provider references and consent metadata, not net-banking passwords or OTPs.

Coverage varies by institution, account type and provider. During discovery, verify supported FIPs, refresh frequency, transaction history window, webhook/polling behavior, sandbox fidelity, data residency, pricing and grievance handling. Build one adapter interface so a provider can be replaced without changing the canonical ledger.

Fallbacks are user-uploaded CSV/PDF statements and manual entry. PDF parsing must be institution/version specific and always show a review screen.

## Credit cards

Account Aggregator coverage for cards must be validated with the selected provider and issuer; do not assume universal availability. Practical fallbacks are issuer statement import and, only with explicit consent, a tightly scoped email-statement ingestion flow. Never collect card PIN, CVV, OTP or full PAN. Retain at most masked identifiers such as last four digits.

Card transactions need pending-versus-posted handling, reversals, refunds, EMI conversions, fees, rewards and statement-cycle metadata. Payment of a card bill is a transfer, not an expense, when the underlying purchases are already imported.

## UPI

There is no generic consumer API that lets an independent app read every UPI transaction from all PSP apps. UPI activity should normally arrive through the linked bank account feed. UTR/RRN and narration can identify the rail and support deduplication. Optional notification or SMS parsing is platform-dependent, permission-sensitive and incomplete; it is a supplement, not a source of truth.

FinanceHub never asks for a UPI PIN and never initiates a payment in this scope.

## Cash

Cash is first-party data and is available now through existing manual expense entry. Future enhancements can add quick-add, recurring cash items, receipt OCR and offline capture with conflict resolution. Cash adjustments should be explicit rather than inferred.

## Statement imports

Provide templates and parsers per institution. Every import has a preview, detected account, date range, row count, validation errors and duplicate estimate before commit. Store the original file only when the user opts in and define a retention period. Password-protected PDFs should be decrypted locally or in short-lived isolated processing; passwords must not be logged or persisted.

## Provider selection checklist

- regulatory role and permitted data flow;
- supported institutions and financial-information types;
- consent artefacts, revocation and audit APIs;
- sandbox and production onboarding lead time;
- data residency, subprocessors and breach process;
- webhook signing and delivery guarantees;
- historical window, refresh quotas and latency;
- pricing, minimum commitments and exit/export terms;
- support, reconciliation and dispute processes.

## Compliance work before production

Obtain specialist legal and security review for the current RBI Account Aggregator framework, Digital Personal Data Protection obligations, CERT-In directions, contractual requirements and any applicable payment/card rules. Document lawful purpose, retention, deletion, incident response, grievance handling and vendor risk. Product copy must distinguish estimated or delayed data from authoritative bank records.
