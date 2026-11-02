# Production observability

FinanceHub uses the platform logs already available in Render, Vercel and Supabase. No paid monitoring SDK or external error tracker is required.

## API request tracing

Every API response includes an `X-Correlation-ID` header. A client may supply an identifier containing only letters, numbers, periods, underscores and hyphens, up to 64 characters. Unsafe values are replaced to prevent log injection.

ASP.NET Core writes structured JSON request-completion records containing:

- correlation ID;
- HTTP method;
- request path without its query string;
- response status;
- elapsed time.

Request bodies, authorization headers, tokens, email addresses, statement contents and financial values are not logged.

Unhandled exceptions are recorded server-side and returned to clients as a sanitized `unexpected_error` response containing the correlation ID. Provider errors and stack traces are never returned to the browser.

## Health endpoints

| Endpoint | Purpose | External dependency |
| --- | --- | --- |
| `GET /health/live` | Confirms the API process can serve requests | None |
| `GET /health/ready` | Confirms required Supabase configuration and Auth availability | Supabase Auth |

Health responses expose only check names, status and duration. They do not expose exception messages, URLs, credentials or provider response bodies.

Use the liveness endpoint for Render's service health check. Use readiness for an external uptime monitor, at a conservative interval such as five minutes. A readiness failure should alert the operator but should not automatically restart or redeploy the service.

## Incident workflow

1. Record the UTC time, failing route, HTTP status and correlation ID shown to the client.
2. Find the same correlation ID in Render's JSON logs.
3. Check the Vercel deployment status and browser console without copying tokens or financial data.
4. Check Supabase Auth, Database and Storage logs for the same time window.
5. If the failure is dependency-related, avoid repeated retries and preserve the first useful trace.
6. Rotate credentials only when evidence indicates exposure; do not paste credentials into an issue or log.

## Privacy boundary

Logs are operational metadata, not an audit ledger. Do not add request/response body logging, JWT claim logging, email logging or SQL parameter logging. Keep provider log access restricted and follow the shortest retention period that still supports incident response.
