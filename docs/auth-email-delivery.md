# Production authentication email delivery

Supabase's built-in email provider is intended for evaluation, not production. It currently limits a project to two authentication emails per hour and may restrict recipients. Password recovery, email confirmation, resend confirmation and magic-link requests share this project-wide quota.

## Required production configuration

In the Supabase project dashboard:

1. Open **Authentication → Emails → SMTP Settings**.
2. Enable custom SMTP.
3. Enter credentials from a transactional email provider such as Resend, AWS SES, Postmark, SendGrid, ZeptoMail or Brevo.
4. Use a dedicated authentication sender and domain, for example `no-reply@auth.example.com`.
5. Configure SPF, DKIM and DMARC with the provider.
6. Open **Authentication → Rate Limits** and select limits supported by the SMTP provider.
7. Keep the password-recovery per-user cooldown at 60 seconds or longer.
8. Enable CAPTCHA for signup, password recovery and passwordless authentication before raising public limits.
9. Send a recovery email to a non-team address and verify the complete update-password flow.

Never commit SMTP passwords. They belong only in Supabase's encrypted SMTP configuration.

## Application behavior

The API calls Supabase's `/auth/v1/recover` endpoint directly and preserves the configured recovery redirect. Rate-limit responses are normalized to HTTP 429 with a stable `email_rate_limited` code. The Angular form shows a clear message and enforces a persisted resend cooldown.

The application cannot bypass a provider-side sending quota. Custom SMTP is therefore required to resolve project-wide production delivery limits.
