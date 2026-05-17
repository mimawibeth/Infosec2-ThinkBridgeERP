# ThinkBridge ERP

A web-based enterprise resource planning system built with ASP.NET Core MVC.

## Features

- **Project & Task Management** – Create projects, assign tasks, track progress
- **Team Collaboration** – Activity feeds, knowledge base, task comments
- **Calendar & Scheduling** – Shared calendar with event management
- **Product Lifecycle** – Track products through development stages
- **Reports & Analytics** – Project, task, and team performance reports
- **Role-Based Access** – Super Admin, Company Admin, Project Manager, Team Member
- **Subscription & Billing** – Plan management with PayMongo payment integration
- **Dark Mode** – Light and dark theme support

## Tech Stack

- ASP.NET Core 8 MVC
- Entity Framework Core + SQL Server
- Vanilla JavaScript
- CSS Custom Properties

## Local Development (no secrets in appsettings)

This project is configured to use `ConnectionStrings:DefaultConnection` from environment variables or .NET User Secrets.

- If `DefaultConnection` is not set and the environment is `Development`, the app falls back to a LocalDB connection automatically.
- For a custom local SQL Server connection string, use User Secrets (recommended):

```powershell
cd ThinkBridge-ERP
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<YOUR_LOCAL_CONNECTION_STRING>"
```

Password reset emails (Resend):

```powershell
dotnet user-secrets set "Resend:ApiKey" "<YOUR_RESEND_API_KEY>"
dotnet user-secrets set "Resend:FromEmail" "ThinkBridge ERP <onboarding@resend.dev>"
```

For production/hosting, set environment variables instead of storing keys in files:

- `Resend__ApiKey`
- `Resend__FromEmail` (optional; defaults to Resend sandbox sender)

Run locally:

```powershell
dotnet run --launch-profile https
```

Apply migrations locally (if needed):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project ThinkBridge-ERP.csproj
```
