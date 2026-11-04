# Redacted institution fixtures

These CSV files are synthetic test data for parser contracts. They contain no real customer names, account numbers, card numbers, references or financial activity.

Supported initial signatures:

- `hdfc-bank-redacted.csv`
- `sbi-bank-redacted.csv`
- `icici-bank-redacted.csv`
- `hdfc-card-redacted.csv`
- `icici-card-redacted.csv`

When an institution changes its export format, add a new redacted fixture before changing the parser. Never commit a real statement, even after manually masking visible values; document metadata and hidden PDF layers can still contain personal data.
