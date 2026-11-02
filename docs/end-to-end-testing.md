# End-to-end acceptance testing

The Playwright suite exercises FinanceHub in a real Chromium browser while intercepting API calls with deterministic responses. It validates routing, forms, token handling and API request contracts without creating production users or changing production data.

## Covered journeys

- unauthenticated route protection;
- login and family-onboarding routing;
- registration confirmation feedback and redirect construction;
- password-recovery rate-limit feedback and cooldown;
- family creation and dashboard entry;
- recurring manual-expense creation and logout.

These tests do not replace a production smoke test for SMTP delivery, Supabase authentication links, hosted database migrations or receipt storage.

## Run locally

From `frontend`:

```bash
npm ci
npm install --no-save --package-lock=false @playwright/test@1.64.1
npx playwright install chromium
npx playwright test
```

To inspect the HTML report:

```bash
npx playwright show-report
```

The Playwright dependency is installed without changing `package.json` or `package-lock.json` so the Angular 17 dependency graph remains unchanged. CI pins the same Playwright version and uploads its report when a test fails.
