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

## Risk Assessment & Mitigation

| Risk | Test Case | Expected Result | Mitigation |
|------|-----------|----------------|------------|
| **Credential Theft** | Login with correct password but wrong TOTP | Access blocked, TOTP code required | Two-Factor Authentication (TOTP via authenticator app) |
| **Brute Force Attack** | 6 failed login attempts in a row | Account locked for 5 minutes | Login attempt policy (progressive lockout) |
| **SQL Injection** | Type `' OR 1=1--` in login form | Login fails, no data exposed | Parameterized queries via Entity Framework Core |
| **CSRF Attack** | Submit a forged form from another site | Request rejected (400) | Anti-forgery tokens on all POST requests |
| **Privilege Escalation** | TeamMember tries to access Admin page | 403 Forbidden | Role-Based Access Control (SuperAdmin, CompanyAdmin, PM, TeamMember) |
| **Password Breach** | Steal password hashes from database | Hashes are unreadable | BCrypt password hashing (salted + slow) |
| **Unattended Session** | Leave browser open and walk away | Auto-logout after 20 min idle | Session inactivity timeout with warning prompt |
