# LinqContraband

<div align="center">

![LinqContraband Icon](icon.png)

### Stop Smuggling Bad Queries into Production

[![NuGet](https://img.shields.io/nuget/v/LinqContraband.svg)](https://www.nuget.org/packages/LinqContraband)
[![Downloads](https://img.shields.io/nuget/dt/LinqContraband.svg)](https://www.nuget.org/packages/LinqContraband)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/github/actions/workflow/status/georgepwall1991/LinqContraband/dotnet.yml?label=build)](https://github.com/georgepwall1991/LinqContraband/actions/workflows/dotnet.yml)
[![Coverage](https://github.com/georgepwall1991/LinqContraband/blob/master/.github/badges/coverage.svg)](https://github.com/georgepwall1991/LinqContraband/actions/workflows/dotnet.yml)

</div>

---

**LinqContraband** is the TSA for your Entity Framework Core queries. It scans your code as you type and confiscates performance killers—like client-side evaluation, N+1 risks, and sync-over-async—before they ever reach production.

### ⚡ Why use LinqContraband?

*   **Zero Runtime Overhead:** It runs entirely at compile-time. No performance cost to your app.
*   **Catch Bugs Early:** Fix N+1 queries and Cartesian explosions in the IDE, not during a 3 AM outage.
*   **Enforce Best Practices:** Acts as an automated code reviewer for your team's data access patterns.
*   **Universal Support:** Works with VS, Rider, VS Code, and CI/CD pipelines. Compatible with all modern EF Core versions.

## 🚀 Installation

Install via NuGet. No configuration required.

```bash
dotnet add package LinqContraband
```

The analyzer will immediately start scanning your code for contraband.

## 👮‍♂️ The Rules

### LC001: The Local Method Smuggler

When EF Core encounters a method it can't translate, it might switch to client-side evaluation (fetching all rows) or throw a runtime exception. This turns a fast SQL query into a massive memory leak.

**❌ The Crime:**

```csharp
// CalculateAge is a local C# method. EF Core doesn't know SQL for it.
var query = db.Users.Where(u => CalculateAge(u.Dob) > 18);
```

**✅ The Fix:**
Extract the logic outside the query.

```csharp
var minDob = DateTime.Now.AddYears(-18);
var query = db.Users.Where(u => u.Dob <= minDob);
```

---

### LC002: Premature Materialization

This is the "Select *" of EF Core. By materializing early, you transfer the entire table over the network, discard 99% of it in memory, and keep the Garbage Collector busy.

**❌ The Crime:**

```csharp
// ToList() executes the query (SELECT * FROM Users).
// Where() then filters millions of rows in memory.
var query = db.Users.ToList().Where(u => u.Age > 18);
```

**✅ The Fix:**
Filter on the database, then materialize.

```csharp
// SELECT * FROM Users WHERE Age > 18
var query = db.Users.Where(u => u.Age > 18).ToList();
```

---

### LC003: Prefer Any() over Count() > 0

Count() > 0 forces the database to scan all matching rows to return a total number (e.g., 5000). Any() generates IF EXISTS (...), allowing the database to stop scanning after finding just one match.

**❌ The Crime:**

```csharp
// Counts 1,000,000 rows just to see if one exists.
if (db.Users.Count() > 0) { ... }
```

**✅ The Fix:**

```csharp
// Checks IF EXISTS (SELECT 1 ...)
if (db.Users.Any()) { ... }
```

---

### LC004: Guid.NewGuid() in Query

SQL Server generates UUIDs differently (sequential vs random) than C#. Using NEWID() in SQL prevents index usage in some cases or forces client-side evaluation if the provider doesn't support translation.

**❌ The Crime:**

```csharp
var query = db.Users.Where(u => u.Id == Guid.NewGuid());
```

**✅ The Fix:**
Generate the value client-side, then pass it in.

```csharp
var newId = Guid.NewGuid();
var query = db.Users.Where(u => u.Id == newId);
```

---

### LC005: Multiple OrderBy Calls

This is a logic bug that acts like a performance bug. The second OrderBy completely ignores the first. The database creates a sorting plan for the first column, then discards it to sort by the second.

**❌ The Crime:**

```csharp
// Sorts by Name, then immediately discards it to sort by Age.
var query = db.Users.OrderBy(u => u.Name).OrderBy(u => u.Age);
```

**✅ The Fix:**
Chain them properly.

```csharp
var query = db.Users.OrderBy(u => u.Name).ThenBy(u => u.Age);
```

---

### LC006: Cartesian Explosion Risk

If User has 10 Orders, and Order has 10 Items, fetching all creates 100 rows per User. With 1000 Users, that's 100,000 rows transferred. `AsSplitQuery` fetches Users, Orders, and Items in 3 separate, clean queries.

**❌ The Crime:**

```csharp
// Fetches Users * Orders * Roles rows.
var query = db.Users.Include(u => u.Orders).Include(u => u.Roles).ToList();
```

**✅ The Fix:**
Use `.AsSplitQuery()` to fetch related data in separate SQL queries.

```csharp
// Fetches Users, then Orders, then Roles (3 queries).
var query = db.Users.Include(u => u.Orders).AsSplitQuery().Include(u => u.Roles).ToList();
```

---

### LC007: N+1 Looper

Database queries have high fixed overhead (latency, connection pooling). Executing 100 queries takes ~100x longer than executing 1 query that fetches 100 items.

**❌ The Crime:**

```csharp
foreach (var id in ids)
{
    // Executes 1 query per ID. Latency kills you here.
    var user = db.Users.Find(id);
}
```

**✅ The Fix:**
Fetch data in bulk outside the loop.

```csharp
// Executes 1 query for all IDs.
var users = db.Users.Where(u => ids.Contains(u.Id)).ToList();
```

---

### LC008: Sync-over-Async

In web apps, threads are a limited resource. Blocking a thread to wait for SQL (I/O) means that thread can't serve other users. Under load, this causes "Thread Starvation", leading to 503 errors even if CPU is low.

**❌ The Crime:**

```csharp
public async Task<List<User>> GetUsersAsync()
{
    // Blocks the thread while waiting for DB.
    return db.Users.ToList();
}
```

**✅ The Fix:**
Use the Async counterpart and await it.

```csharp
public async Task<List<User>> GetUsersAsync()
{
    // Frees up the thread while waiting.
    return await db.Users.ToListAsync();
}
```

---

### LC009: The Tracking Tax

EF Core takes a "snapshot" of every entity it fetches to detect changes. For a read-only dashboard, this snapshot process consumes CPU and doubles the memory usage for every row.

**❌ The Crime:**

```csharp
public List<User> GetUsers()
{
    // EF Core tracks these entities, but we never modify them.
    return db.Users.ToList();
}
```

**✅ The Fix:**
Add `.AsNoTracking()` to the query.

```csharp
public List<User> GetUsers()
{
    // Pure read. No tracking overhead.
    return db.Users.AsNoTracking().ToList();
}
```

---

### LC010: SaveChanges Loop Tax

Opening and committing a database transaction is an expensive operation. Doing this inside a loop (e.g., for 100 items) means 100 separate transactions, which can be 1000x slower than a single batched commit.

**❌ The Crime:**

```csharp
foreach (var user in users)
{
    user.LastLogin = DateTime.Now;
    // Opens a transaction and commits for EVERY user.
    db.SaveChanges();
}
```

**✅ The Fix:**

Batch the changes and save once.

```csharp
foreach (var user in users)
{
    user.LastLogin = DateTime.Now;
}
// One transaction, one roundtrip.
db.SaveChanges();
```

## ⚙️ Configuration

You can configure the severity of these rules in your `.editorconfig` file:

```ini
[*.cs]
dotnet_diagnostic.LC001.severity = error
dotnet_diagnostic.LC002.severity = error
dotnet_diagnostic.LC003.severity = warning
```

## 🤝 Contributing

Found a new way to smuggle bad queries? [Open an issue](https://github.com/georgepwall1991/LinqContraband/issues) or submit a
PR!

License: [MIT](LICENSE)
