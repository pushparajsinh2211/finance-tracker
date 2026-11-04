# Production verification

FinanceHub performs a privacy-safe production smoke check after the `CI` workflow succeeds on `master`.

## Checks

The workflow verifies only public operational surfaces:

- the Vercel frontend returns the Angular application shell;
- the Render API liveness endpoint reports `healthy`;
- the API readiness endpoint reports `healthy`;
- the `supabase-auth` readiness dependency reports `healthy`.

It never signs in, reads user data, or uses application credentials.

## Triggering a check

The check runs automatically after successful `master` CI. It can also be run manually from **Actions → Production smoke verification → Run workflow**.

The probes retry for up to three minutes to tolerate normal Vercel propagation and Render free-tier cold starts. A failed smoke check does not roll back a deployment; inspect the workflow output and follow the incident steps in [Production observability](observability.md).

## Authenticated release verification

Automated CI covers the mocked browser journey. After a UI release, perform one authenticated production pass with a non-sensitive test account:

1. sign in and open the dashboard;
2. open the expense date calendar by clicking anywhere in the date field;
3. navigate a month and year, then select a day;
4. open the category picker and select a category;
5. save an expense and confirm it appears once;
6. verify dashboard, budget, goal/loan, and transaction filter controls at desktop and mobile widths.

Do not use production financial credentials or real statement files for release smoke testing.
