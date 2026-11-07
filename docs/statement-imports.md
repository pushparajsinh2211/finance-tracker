# Statement imports, reconciliation, OCR and retention

FinanceHub supports user-initiated CSV and PDF statement imports. This is not automatic bank, card, Account Aggregator or UPI connectivity.

## Institution profiles

The parser identifies the following initial, redacted CSV layouts by normalized header signatures:

- State Bank of India bank statements;
- HDFC Bank account statements;
- ICICI Bank account statements;
- HDFC credit-card statements;
- ICICI credit-card statements.

It falls back to the generic parser when a recognized date, narration, amount and direction layout is present. Banks and issuers change exports without notice, so every profile is backed by a synthetic, redacted fixture in `backend/FamilyLedger.Api.Tests/Fixtures`. The fixtures contain no real names, account numbers or transactions.

A profile match does not certify a statement as authoritative. Users must review the preview and reconcile it against the source statement.

## Generic PDF table fallback

When a text PDF does not match the strict row parser, FinanceHub also reconstructs rows from positioned PDF words. The fallback supports common Indian bank and card patterns, including date-led rows, decimal debit/credit amounts, `DR`/`CR` markers, and running balances.

For bank statements, direction is accepted only when it is explicit or the running-balance change verifies the amount. Ambiguous candidates are skipped and reported; mapped rows are marked for review. This improves compatibility with ICICI and other issuers without silently guessing transactions. It is still a best-effort importer, not production bank connectivity, and a bank CSV export remains the most reliable fallback for an unsupported layout.

## Scanned PDF OCR

Text PDFs are parsed with PdfPig. If a PDF contains too little extractable text, the API renders at most the first 20 pages with Poppler and runs Tesseract locally inside the API container.

- statement content is not sent to a third-party OCR API;
- temporary PDFs and page images are removed after processing;
- OCR rows are always marked as needing review;
- files remain limited to 10 MB and 100 PDF pages;
- password-protected scanned PDFs are not passed to command-line tools, preventing password exposure through process arguments;
- unclear scans may still need CSV export or a dedicated parser.

## Reconciliation

For bank CSVs with running balances, preview calculates:

`opening balance + credits - debits = calculated closing balance`

It compares the calculated closing balance with the final reported balance. A variance up to ₹0.01 is considered matched. A mismatch is displayed prominently and requires explicit confirmation before commit. Credit-card and generic PDF reconciliation remains unavailable unless issuer cycle totals can be read reliably; debit and credit totals are still shown.

## Original statement retention

The default is **do not retain**. Parsing and importing do not persist the uploaded original.

A user may explicitly retain the original for 30 or 90 days. Retained files:

- are stored in the private `financehub-statements` Supabase bucket;
- use a user-prefixed object path protected by storage RLS;
- have user-owned metadata protected by table RLS;
- can be deleted immediately from the import completion screen;
- are purged after expiry by the API cleanup service when `Supabase__ServiceRoleKey` is configured.

Passwords are never retained. Parsed immutable rows and canonical transactions follow the existing account export/deletion lifecycle independently of the optional original file.
