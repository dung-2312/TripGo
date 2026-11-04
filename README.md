# TripGo - Bus Ticket Booking Platform

> **Course**: PRN222 (.NET Application Development)  
> **Platform**: .NET 8 (C# 12), Blazor Web App (Interactive Server), Entity Framework Core 8, Microsoft SQL Server

---

## 1. Project Overview

**TripGo** is a modern, reliable online bus ticket booking platform designed to streamline travel management for passengers and transport operators. The system offers intuitive trip discovery, real-time seat selection with concurrency protection, multi-passenger booking workflows, and booking management.

Built adhering to **Clean Architecture** principles, TripGo ensures high maintainability, testability, and enterprise-grade security.

---

## 2. Key Features

### 🚌 Customer Portal

- **Trip Search & Filter**: Search trips by departure city, destination, and travel date with real-time availability and pricing.
- **Interactive Seat Selection**: Visual seat maps with instant seat-hold tokens to prevent double-booking.
- **Passenger & Booking Management**: Input passenger details, select pick-up points, and generate unique booking codes.
- **Ticket Lookup ("My Tickets")**: Look up booking history, view e-tickets, and verify payment/confirmation status.

### 🛡️ Core Hardening & Concurrency (Phase 3A)

- **Session-Based Booking Ownership**: Secure HttpOnly cookies bound to booking session tokens, preventing unauthorized access.
- **Optimistic Concurrency**: Prevents race conditions during simultaneous seat selection on high-demand routes.
- **Automatic Expiration Worker**: Background hosted service (`BookingExpirationWorker`) automatically releases expired seat holds.
- **Pre-flight & Post-flight Database Auditing**: Isolated migration and verification scripts to ensure database integrity.

### 🏢 Operator & Admin Management

- Route and schedule management.
- Fleet and vehicle seat layout configuration.
- Real-time booking statistics and capacity monitoring.

---

## 3. Technology Stack & Architecture

TripGo is structured following **Clean Architecture (N-Tier)**:

```
TripGo/
├── src/
│   ├── Tripgo.Domain/             # Enterprise Entities, Enums, Value Objects
│   │   ├── Entities/              # Trip, Route, Bus, Seat, Booking, Passenger, Ticket
│   │   └── Enums/                 # BookingStatus, SeatStatus, TripStatus
│   │
│   ├── Tripgo.Application/          # Business Logic, Use Cases & DTOs
│   │   ├── DTOs/                  # BookingDto, TripDto, PassengerDto
│   │   ├── Interfaces/            # Service & Repository Contracts
│   │   └── Services/              # BookingService, TripService
│   │
│   ├── Tripgo.Infrastructure/       # Persistence, EF Core & External Services
│   │   ├── Migrations/            # EF Core Schema Migrations
│   │   ├── Persistence/           # AppDbContext, Repositories, Configurations
│   │   └── ...
│   │
│   └── Tripgo.Web/                # Presentation Layer (Blazor Web App)
│       ├── Components/            # Razor Components, Layouts, & Pages
│       │   ├── Pages/             # SearchTrips.razor, MyTickets.razor
│       │   └── Layout/            # NavMenu, MainLayout
│       ├── Services/              # BookingExpirationWorker, UI State
│       └── appsettings.json       # App & Connection Configurations
│
├── tests/
│   └── Tripgo.Tests/              # xUnit Test Suite (BookingCoreTests, etc.)
│
├── scripts/
│   └── data/                      # SQL Data Preparation, Seeding & Migrations
│
├── docs/                          # Architecture & Phase Hardening Documentation
│
├── .gitignore
├── README.md
└── Tripgo.sln
```

---

## 4. Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.
- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (v17.8+) with _ASP.NET and web development_ workload, or [JetBrains Rider] / [VS Code].
- [Microsoft SQL Server](https://www.microsoft.com/sql-server) (2019/2022 or LocalDB).

### Step-by-Step Setup

1. **Clone the repository**:

   ```bash
   git clone https://github.com/dung-2312/TripGo.git
   cd TripGo
   ```

2. **Configure Database Connection**:
   Update your database connection string in `src/Tripgo.Web/appsettings.Development.json`:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=TripgoDb;Trusted_Connection=True;TrustServerCertificate=True;"
     }
   }
   ```

3. **Restore Dependencies**:

   ```bash
   dotnet restore
   ```

4. **Build the Solution**:

   ```bash
   dotnet build
   ```

5. **Run Database Migrations / Scripts**:
   Execute the migration scripts located in `scripts/data/` or apply EF Core migrations:

   ```bash
   dotnet ef database update --project src/Tripgo.Infrastructure --startup-project src/Tripgo.Web
   ```

6. **Run Unit Tests**:
   Verify system integrity by executing the test suite:

   ```bash
   dotnet test
   ```

7. **Launch the Application**:
   ```bash
   dotnet run --project src/Tripgo.Web
   ```
   Open your browser and navigate to `https://localhost:7082` (or the port specified in console output).

---

## 5. Development & Contribution Workflow

To maintain code quality and prevent regressions, all team members follow the standard Git Flow:

1. **Keep `main` Clean**: Never push directly to the `main` branch.
2. **Create a Feature Branch**:
   ```bash
   git checkout main
   git pull origin main
   git checkout -b feature/<your-name>/<feature-description>
   ```
3. **Commit with Conventional Commit Messages**:
   - `feat: ...` for new features.
   - `fix: ...` for bug fixes.
   - `docs: ...` for documentation updates.
   - `test: ...` for adding/updating tests.
4. **Create a Pull Request (PR)**:
   - Push your branch to `origin`:
     ```bash
     git push -u origin feature/<your-name>/<feature-description>
     ```
   - Open a Pull Request into `main` on GitHub for peer review and merge.

---

## 6. Project Members & Roles

| Member                          | Student ID | Primary Role                                    |
| :------------------------------ | :--------- | :---------------------------------------------- |
| **Nguyễn Tiến Dũng** _(Leader)_ | QE190275   | Architecture, Backend Core & Database Hardening |
| **Nguyễn Văn Điệp**             | QE180203   | Customer UI, Booking Flow & Documentation       |
| **Đoàn Đệ**                     | QE190091   | Auth System & User Management                   |
| **Nguyễn Anh Tuấn**             | QE190003   | Admin Portal & Reporting                        |

---

## 7. License & Acknowledgements

This project is developed as part of the academic curriculum for the **PRN222** course at **FPT University**.
