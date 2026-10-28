# FamilyLedger - Personal & Family Finance Tracker

FamilyLedger is a comprehensive, full-stack personal and family finance management application. Built natively on **ASP.NET Core 8**, **Angular 17**, and **Supabase (PostgreSQL)**, this application bypasses expensive subscription models by leveraging extremely robust Row-Level Security (RLS), Native DB Cron Jobs, and Cloud Storage pipelines strictly optimized for free-tier deployments.

## 🚀 Features

### Phase 1: Foundational Ledger & Family Sync
* **Complete Supabase Auth:** JWT-based authentication bridging Angular with ASP.NET Core API middleware.
* **Family Management:** Generate secure multi-user invite codes allowing dependents to join families securely.
* **Row-Level Security (RLS):** Total data isolation at the Postgres level. Users explicitly only see data mapped to their family ledger.
* **Transaction Dashboard:** Glassmorphism UI tracking expenses automatically separated by Month and Category tags. Recurring expenses are natively supported.

### Phase 2: Budgets & Automation
* **Categorical Budgets:** Map maximum spending limits across any specific active category per month.
* **Live Progress UI:** Color-shifting UI sliders warning you automatically if a budget crosses 75% or 90% consumption.
* **Native Postgres Cron (`pg_cron`)**: Instead of expensive `.NET` background workers, recurring transactions organically duplicate precisely exact-matched a month later inside the Database.

### Phase 3: Advanced Financial Tools
* **EMI & Loans Tracker:** Independently monitor loan principals, timelines, and monthly EMI deduction spans.
* **Savings Goals:** Visual progress trackers mapping exact current completion percentages toward larger targeted purchases.
* **Cloud Receipt Storage:** Natively upload image/PDF documentation directly attached to transactions. Files autonomously sync to authenticated Supabase buckets.
* **Family Head Aggregation:** Special RPC queries computing cross-dependent analytics specifically unlocking aggregated summary totals for Family Heads.

### Phase 4: Polish & Notifications
* **Postgres Live Triggers:** Database autonomously fires notifications into the UI whenever a Budget is exceeded or a new member joins your Ledger framework.
* **PWA Capable:** Complete Web-App Manifest mapping enabling direct standard mobile Home-Screen installation bypassing App Store fees natively!

## 💻 Tech Stack
- **Frontend**: Angular 17, RxJS, HTML5/Vanilla CSS (Glassmorphism & native variables).
- **Backend**: C# ASP.NET Core 8 Web API.
- **Database / Auth**: PostgreSQL via Supabase, `pg_cron` extensions, PostgREST API wrapper logic.

## 🛠️ Local Development

### Prerequisites

- Docker Desktop
- Node.js and npm
- .NET 8 SDK

### 1. Database

Start the local Supabase stack from the repository root. The local database is isolated from the hosted Supabase project and applies the migrations in `supabase/migrations`.

```bash
npx supabase start
```

### 2. Backend (.NET)

Create `backend/FamilyLedger.Api/appsettings.Development.json` with the local API URL and anon key shown by `npx supabase status`:

```json
{
  "Supabase": {
    "Url": "http://127.0.0.1:54321",
    "AnonKey": "YOUR_LOCAL_ANON_KEY"
  }
}
```

Then run the API from the repository root:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5099 \
dotnet run --project backend/FamilyLedger.Api/FamilyLedger.Api.csproj --no-launch-profile
```

To enable family invitation emails, configure SMTP settings through environment variables:
```bash
Email__From=your-sender@example.com
Email__FromName=FamilyLedger
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__EnableSsl=true
Email__Smtp__Username=your-smtp-username
Email__Smtp__Password=your-smtp-password
```
If these values are not configured, the app will ask the Family Head to copy and share the invite code instead of pretending an email was sent.

### 3. Frontend (Angular)

Install dependencies once, then start Angular. Development builds automatically use the local URLs from `environment.development.ts`.

```bash
cd frontend
npm ci
npm start
```

Open `http://127.0.0.1:4200`.

To stop the local database services later, run `npx supabase stop` from the repository root.

---

*FamilyLedger was successfully architected using strict free-tier deployment guidelines ensuring zero operational overhead cost.*
