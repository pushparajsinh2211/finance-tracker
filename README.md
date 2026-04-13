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

### 1. Database
Make sure you have Supabase CLI installed and linked to your project. Push your local schema structure up into your cloud:
```bash
npx supabase db push
```

### 2. Backend (.NET)
Navigate to the Web API folder and run it:
```bash
cd backend/FamilyLedger.Api
dotnet run
```

### 3. Frontend (Angular)
Serve the application locally:
```bash
cd frontend
npm start
```
Your UI will be successfully running at `http://localhost:4200`!

---

*FamilyLedger was successfully architected using strict free-tier deployment guidelines ensuring zero operational overhead cost.*
