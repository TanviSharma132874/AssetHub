# AssetHub

A three-module asset management system built with **.NET 10** (ASP.NET Core minimal APIs, EF Core + SQLite).
Each module is its own web app with its **own database** and **own UI**; all three share one JWT secret, so a single login works everywhere.

| Module | Project | Port | Database | Owner |
|---|---|---|---|---|
| **A. Identity & Organization** | `AssetHub.Identity` | 5001 | `identity.db` | Tanvi |
| **B. Asset & Allocation** | `AssetHub.Assets` | 5002 | `assets.db` | Person 2 |
| **C. Booking & Audit** | `AssetHub.Booking` | 5003 | `booking.db` | Person 3 |

`AssetHub.Shared` is a small class library used by all three (JWT validation, error handling, input guards, UTC date handling, the Identity client). `SharedUI/` is the shared UI template (CSS + a small JS toolkit); it is copied into each module's `wwwroot/shared`.

## Run it

Requires the **.NET 10 SDK**.

```bash
./run-all.sh          # Linux / macOS / Git Bash
.\run-all.ps1         # Windows PowerShell
```

Or start the modules yourself, **Identity first** (open three terminals):

```bash
cd src/AssetHub.Identity && dotnet run
cd src/AssetHub.Assets   && dotnet run
cd src/AssetHub.Booking  && dotnet run
```

Open http://localhost:5001 and sign in with the seeded admin: **admin@assethub.local / Admin@123** (change it in `appsettings.json` → `Seed` before the first run).
Databases are created automatically on first start (`EnsureCreated`) and seeded with demo departments, asset categories and bookable resources. Delete the `*.db` files to reset.

## What each module does

### A. Identity & Organization
- Sign up / sign in with JWT (PBKDF2 password hashing via ASP.NET Core `PasswordHasher`). New accounts are always **Employee**.
- **Admin-only role promotion** (`PUT /api/users/{id}/role`): Employee / Manager / Admin. You cannot change your own role or remove the last admin. The affected user signs in again to get the new role.
- Departments and Asset Categories (CRUD; categories carry the tag prefix, e.g. `LAP`).
- Employee directory with search and filters; admins can edit people and deactivate accounts.

### B. Asset & Allocation
- **Registration with auto-generated tags**: `LAP-0001`, `LAP-0002`, … per category prefix (counter table, serialised by a lock; unique index on the tag).
- **Allocation with conflict detection**: an asset that is already allocated, under maintenance or retired is rejected with a clear message. The check runs under a lock *and* a filtered unique index (`AssetId` where `ReturnedAt IS NULL`) makes double allocation impossible at database level.
- **Transfer workflow**: holder (or manager) requests → manager approves/rejects. Approval closes the old allocation and opens the new one in one transaction.
- **Maintenance approval flow**: Pending → Approved (asset goes *Under maintenance* and is taken off its holder) → Completed (asset available again, or retired if beyond repair). Pending requests can be rejected.

### C. Booking & Audit
- **Resource booking with overlap validation**: for the same resource, two confirmed bookings may not overlap (back-to-back is fine). Also rejects past start times, end ≤ start and bookings longer than `Booking:MaxHours`.
- **Audit cycles**: a manager starts a cycle (all non-retired assets, or one category); Module C fetches the asset list from Module B and snapshots it. Each asset is marked **Verified / Missing / Damaged** (remarks required for Missing/Damaged). A cycle can be closed (with a warning if assets are still pending).
- **Basic notifications**: booking confirmed / cancelled, a reminder 30 minutes before a booking starts (background service), audit cycle created / closed, and an alert when an asset is marked Missing or Damaged. Shown in-app with an unread badge.

## How the modules talk to each other

- **Browser → API**: each UI calls its own API. Sign-in, the employee list and the category list come from the Identity API (CORS is enabled there for ports 5001–5003).
- **Single sign-on**: the sidebar's "Other modules" links pass your token in the URL fragment (`#sso=…`, never sent to a server) so you are not asked to log in again on another port.
- **Server → server**: Module B calls Identity (validate employee / category), Module C calls Assets (asset list for audits). The caller's bearer token is forwarded. If the other service is down, the API answers `503` with a readable message.
- Modules keep **snapshots** (employee name, category name, asset tag) instead of foreign keys across databases, so history stays readable even if the source record changes.

## Roles

| Role | Can do |
|---|---|
| Employee | See assets and directory, view own allocations, request transfers/maintenance for assets they hold, book resources, cancel own bookings |
| Manager | Everything an Employee can, plus register/edit/retire assets, allocate, approve transfers and maintenance, manage resources, run audits, cancel any booking |
| Admin | Everything a Manager can, plus departments, categories, user edits and **role promotion** |

## Configuration

- Ports, database file names and service URLs are in each project's `appsettings.json`.
- `Jwt:Key` **must be identical in all three** `appsettings.json` files (min. 32 chars). Replace the development key before any real deployment, and use `dotnet user-secrets` or environment variables (`Jwt__Key`) instead of committing it.
- Browser URLs of the modules are in each `wwwroot/config.js` (change them there if you change ports).
- Updating the shared UI: edit `SharedUI/`, then copy it into each module:
  ```bash
  for m in Identity Assets Booking; do cp -r SharedUI/* src/AssetHub.$m/wwwroot/shared/; done
  ```

## API quick reference

```
Identity  POST /api/auth/signup | /api/auth/login   GET /api/auth/me
          GET  /api/users[?search&departmentId&role]   GET /api/users/{id}
          PUT  /api/users/{id} (admin)                 PUT /api/users/{id}/role (admin)
          /api/departments  /api/categories            (GET all; write = admin)
Assets    GET/POST /api/assets   GET/PUT /api/assets/{id}   POST /api/assets/{id}/retire
          GET/POST /api/allocations   POST /api/allocations/{id}/return   GET /api/allocations/mine
          GET/POST /api/transfers   POST /api/transfers/{id}/approve|reject|cancel
          GET/POST /api/maintenance POST /api/maintenance/{id}/approve|reject|complete
          GET /api/dashboard
Booking   GET/POST/PUT /api/resources   GET/POST /api/bookings   POST /api/bookings/{id}/cancel
          GET/POST /api/audits   GET /api/audits/{id}   PUT /api/audits/{id}/items/{itemId}
          POST /api/audits/{id}/close   GET /api/notifications   POST /api/notifications/{id}/read
```
Every error comes back as `{ "error": "message" }` with a meaningful status (400 validation, 401, 403, 404, 409 conflict, 503 other module unreachable).

## Suggested demo flow

1. Sign in as admin on :5001. Create a Manager: sign up a second account in another browser/incognito, then **Employee Directory → Change role → Manager**.
2. On :5002 (Assets) register a laptop (`LAP-0001`), allocate it to an employee, then try to allocate it again → conflict error.
3. As that employee request a transfer; as manager approve it and check the history in the asset's *Details*.
4. Report a fault, approve the maintenance request (asset goes *Under maintenance*), then complete it.
5. On :5003 book *Conference Room A*, then try an overlapping slot → rejected. Check *Notifications*.
6. Start an audit cycle, mark one asset Missing, close the cycle.

## Known limitations

- Not yet compiled or run by the author of this scaffold; see the note in the project hand-over. Build with `dotnet build AssetHub.sln` and report any error.
- SQLite + in-process locks assume **one instance per module**. For multiple instances, move to PostgreSQL/SQL Server and replace `WriteGate` with database transactions.
- Notifications live in Module C only (Modules A and B do not raise them).
- Tokens are stored in `localStorage` (fine for a course project; use httpOnly cookies for production). A role change takes effect at the user's next sign-in.
- Schema is created with `EnsureCreated`; add EF migrations if you need to evolve the schema without deleting the `.db` files.

## Visual Studio

`AssetHub.Shared` is a class library and cannot be started. Right-click the solution → **Configure Startup Projects… → Multiple startup projects**, set **Identity, Assets, Booking** to *Start* (Identity first), then press F5. Or run one project at a time by choosing it as the startup project.
