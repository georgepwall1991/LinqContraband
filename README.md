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

**LinqContraband** is the TSA for your Entity Framework Core queries. It scans your code as you type and confiscates
performance killers—like client-side evaluation, N+1 risks, and sync-over-async—before they ever reach production.

### ⚡ Why use LinqContraband?

* **Zero Runtime Overhead:** It runs entirely at compile-time. No performance cost to your app.
* **Catch Bugs Early:** Fix N+1 queries and Cartesian explosions in the IDE, not during a 3 AM outage.
* **Enforce Best Practices:** Acts as an automated code reviewer for your team's data access patterns.
* **Universal Support:** Works with VS, Rider, VS Code, and CI/CD pipelines. Compatible with all modern EF Core
  versions.

## 🚀 Installation

Install via NuGet. No configuration required.

```bash
dotnet add package LinqContraband
```

The analyzer will immediately start scanning your code for contraband.

## 👮‍♂️ The Rules

### LC001: The Local Method Smuggler

When EF Core encounters a method it can't translate, it might switch to client-side evaluation (fetching all rows) or
throw a runtime exception. This turns a fast SQL query into a massive memory leak.

**👶 Explain it like I'm a ten year old:** Imagine hiring a translator to translate a book into Spanish, but you used
made-up slang words they don't know. They can't finish the job, so they hand you the *entire* dictionary and say "You
figure it out." You have to read the whole dictionary just to find one word.

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

This is the "Select *" of EF Core. By materializing early, you transfer the entire table over the network, discard 99%
of it in memory, and keep the Garbage Collector busy.

**👶 Explain it like I'm a ten year old:** Imagine you want a pepperoni pizza. Instead of ordering just pepperoni, you
order a pizza with *every single topping in the restaurant*. When it arrives, you have to spend an hour picking off the
anchovies, pineapple, and mushrooms before you can eat. It’s a waste of food and time.

**❌ The Crime:**

```csharp
// ToList() executes the query (SELECT * FROM Users).
// Where() then filters millions of rows in memory.
var query = db.Users.ToList().Where(u => u.Age > 18);

// Same crime with other materializers: AsEnumerable, ToDictionary, etc.
var query2 = db.Users.AsEnumerable().Where(u => u.Age > 18);
var query3 = db.Users.ToDictionary(u => u.Id).Where(kvp => kvp.Value.Age > 18);
```

**✅ The Fix:**
Filter on the database, then materialize.

```csharp
// SELECT * FROM Users WHERE Age > 18
var query = db.Users.Where(u => u.Age > 18).ToList();
var query2 = db.Users.Where(u => u.Age > 18).ToDictionary(u => u.Id);
```

---

### LC003: Prefer Any() over Count() > 0

Count() > 0 forces the database to scan all matching rows to return a total number (e.g., 5000). Any() generates IF
EXISTS (...), allowing the database to stop scanning after finding just one match.

**👶 Explain it like I'm a ten year old:** Imagine you want to know if there are any cookies left in the jar. Count() > 0
is like dumping the entire jar onto the table and counting 500 cookies one by one just to say "Yes". Any() is like
opening the lid, seeing one cookie, and saying "Yes" immediately.

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

### LC004: Deferred Execution Leak

Passing `IQueryable<T>` to a method that takes `IEnumerable<T>` forces implicit materialization if the method iterates
it. This prevents you from composing the query further (e.g., adding `.Where()` or `.Take()`) inside that method.

**👶 Explain it like I'm a ten year old:** Imagine you have a coupon for "Build Your Own Burger". You give it to the
chef, but instead of letting you choose toppings, he immediately hands you a plain burger and says "Too late, I already
cooked it."

**❌ The Crime:**

```csharp
public void ProcessUsers(IEnumerable<User> users)
{
    // Iterates and fetches ALL users from DB immediately.
    foreach(var u in users) { ... }
}

// Passing IQueryable to IEnumerable parameter.
ProcessUsers(db.Users);
```

**✅ The Fix:**

Change the parameter to `IQueryable<T>` to allow composition, or explicitly call `.ToList()` if you *intend* to fetch
everything.

```csharp
public void ProcessUsers(IQueryable<User> users)
{
    // Now we can filter! SELECT ... WHERE Age > 10
    foreach(var u in users.Where(x => x.Age > 10)) { ... }
}
```

---

### LC005: Multiple OrderBy Calls

This is a logic bug that acts like a performance bug. The second OrderBy completely ignores the first. The database
creates a sorting plan for the first column, then discards it to sort by the second.

**👶 Explain it like I'm a ten year old:** Imagine telling someone to sort a deck of cards by Suit (Hearts, Spades...).
As soon as they finish, you say "Actually, sort them by Number (2, 3, 4...) instead." They did all that work for the
first sort for nothing because you changed the rules.

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

If User has 10 Orders, and Order has 10 Items, fetching all creates 100 rows per User. With 1000 Users, that's 100,000
rows transferred. `AsSplitQuery` fetches Users, Orders, and Items in 3 separate, clean queries.

**👶 Explain it like I'm a ten year old:** Imagine a teacher asks 30 students what they ate. Instead of getting 30
answers, she asks every student to list every single fry they ate individually. You end up with thousands of answers ("I
ate fry #1", "I ate fry #2") instead of just "I had fries".

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

Database queries have high fixed overhead (latency, connection pooling). Executing 100 queries takes ~100x longer than
executing 1 query that fetches 100 items.

**👶 Explain it like I'm a ten year old:** Imagine you need 10 eggs. You drive to the store, buy *one* egg, drive home.
Drive back, buy *one* egg, drive home. You do this 10 times. You spend all day driving instead of just buying the carton
at once.

**❌ The Crime:**

```csharp
foreach (var id in ids)
{
    // Executes 1 query per ID. Latency kills you here.
    var user = db.Users.Find(id);
}

// Also flags async streams that materialize inside the loop
await foreach (var user in db.Users.AsAsyncEnumerable())
{
    var count = db.Users.Count();
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

In web apps, threads are a limited resource. Blocking a thread to wait for SQL (I/O) means that thread can't serve other
users. Under load, this causes "Thread Starvation", leading to 503 errors even if CPU is low.

**👶 Explain it like I'm a ten year old:** Imagine a waiter taking your order, then walking into the kitchen and staring
at the chef for 20 minutes until the food is ready. No one else gets served. That's Sync-over-Async. Async means the
waiter takes the order and goes to serve other tables while the food cooks.

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

EF Core takes a "snapshot" of every entity it fetches to detect changes. For a read-only dashboard, this snapshot
process consumes CPU and doubles the memory usage for every row.

**👶 Explain it like I'm a ten year old:** Imagine you go to a museum. You promise not to touch anything. But security
guards still follow you and take high-resolution photos of every painting you look at, just in case you decide to draw a
mustache on one. It wastes their time and memory.

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

    // Or, if you need identity resolution without full tracking:
    return db.Users.AsNoTrackingWithIdentityResolution().ToList();
}
```

---

### LC010: SaveChanges Loop Tax

Opening and committing a database transaction is an expensive operation. Doing this inside a loop (e.g., for 100 items)
means 100 separate transactions, which can be 1000x slower than a single batched commit.

**👶 Explain it like I'm a ten year old:** Imagine mailing 100 letters. Instead of putting them all in the mailbox at
once, you put one in, wait for the mailman to pick it up, then put the next one in. It takes 100 days to mail your
invites!

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

---

### LC011: Entity Missing Primary Key

Entities in EF Core require a Primary Key to track identity. If you don't define one, EF Core might throw a runtime
exception or prevent you from updating the record later.

**👶 Explain it like I'm a ten year old:** Imagine a library where books have no titles or ISBN numbers. You ask for a
book, but because there's no unique way to identify it, the librarian can't find it, or worse, gives you the wrong one.

**❌ The Crime:**

```csharp
public class Product
{
    // No 'Id', 'ProductId', or [Key] attribute defined.
    public string Name { get; set; }
}
```

**✅ The Fix:**

Define a primary key using the `Id` convention, `[Key]` attribute, or Fluent API.

```csharp
// 1. Convention: Id or {ClassName}Id
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
}

// 2. Attribute: [Key]
public class Product
{
    [Key]
    public int ProductCode { get; set; }
    public string Name { get; set; }
}

// 3. Fluent API (in OnModelCreating)
modelBuilder.Entity<Product>().HasKey(p => p.ProductCode);

// 4. Separate Configuration (IEntityTypeConfiguration<T>)
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.ProductCode);
    }
}
```

---

### LC012: Optimize Bulk Delete

`RemoveRange()` fetches entities into memory before deleting them one by one (or in batches). `ExecuteDelete()` (EF Core
7+) performs a direct SQL DELETE, which is orders of magnitude faster.

**👶 Explain it like I'm a ten year old:** Imagine you want to throw away a pile of old magazines. `RemoveRange` is like
picking up each magazine, reading the cover, and then throwing it in the bin. `ExecuteDelete` is like dumping the whole
box in the bin at once.

**❌ The Crime:**

```csharp
var oldUsers = db.Users.Where(u => u.LastLogin < DateTime.Now.AddYears(-1));
// Fetches all old users into memory, then deletes them.
db.Users.RemoveRange(oldUsers);
```

**✅ The Fix:**

Use `ExecuteDelete()` for direct SQL execution.

```csharp
// Executes: DELETE FROM Users WHERE LastLogin < ...
db.Users.Where(u => u.LastLogin < DateTime.Now.AddYears(-1)).ExecuteDelete();
```

**⚠️ Warning:** `ExecuteDelete` bypasses EF Core Change Tracking, so `Deleted` events and client-side cascades won't
fire. This analyzer does not offer an automatic code fix because switching to `ExecuteDelete` changes the semantic
behavior of your application (by skipping interceptors and events). You must manually verify it is safe to use.

---

### LC013: Disposed Context Query

EF Core queries are deferred, meaning they don't execute until you iterate them. If you build a query using a local
`DbContext` that is disposed (via `using`) and return that query, it will explode when the caller tries to use it.

**👶 Explain it like I'm a ten year old:** Imagine buying a ticket to a movie. But the ticket is only valid *inside* the
ticket booth. As soon as you walk out to the theater (return the query), the ticket dissolves in your hand.

**❌ The Crime:**

```csharp
public IQueryable<User> GetUsers(bool adultsOnly)
{
    using var db = new AppDbContext();

    // The analyzer catches simple returns:
    // return db.Users;

    // AND sneaky violations in conditional logic:
    return adultsOnly
        ? db.Users.Where(u => u.Age >= 18)
        : db.Users;
}
```

**✅ The Fix:**

Materialize the results (e.g., `.ToList()`) while the context is still alive.

```csharp
public List<User> GetUsers(bool adultsOnly)
{
    using var db = new AppDbContext();

    var query = adultsOnly
        ? db.Users.Where(u => u.Age >= 18)
        : db.Users;

    // Executes the query immediately. Safe to return.
    return query.ToList();
}
```

---

### LC014: Avoid String Case Conversion in Queries

Using `ToLower()` or `ToUpper()` inside a LINQ query (e.g., `Where` clause) prevents the database from using an index on
that column. This forces a full table scan, which is significantly slower for large datasets.

**👶 Explain it like I'm a ten year old:** Imagine looking for "John" in a phone book. If you look for "John", you can
jump straight to 'J'. But if you decide to convert every single name in the book to lowercase first, you have to read
*every single name* from A to Z to check if it matches "john".

**❌ The Crime:**

```csharp
// Forces a full table scan because the index on 'Name' cannot be used.
var user = db.Users.Where(u => u.Name.ToLower() == "john").FirstOrDefault();
```

**✅ The Fix:**

Use `string.Equals` with a case-insensitive comparison, or configure the database collation to be case-insensitive.

```csharp
// 1. Use string.Equals (translated to efficient SQL if supported)
var user = db.Users.Where(u => string.Equals(u.Name, "john", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

// 2. Or, rely on DB collation (if case-insensitive by default)
var user = db.Users.Where(u => u.Name == "john").FirstOrDefault();
```

---

### LC015: Missing OrderBy Before Skip/Last

Pagination (`Skip`/`Take`) and fetching the `Last` item rely on a specific sort order. If the query is unordered, the
database can return results in any random order, making pagination unpredictable and `Last()` results non-deterministic.

**👶 Explain it like I'm a ten year old:** Imagine a teacher asks you to "Skip the first 5 students and pick the next
one." If the students are standing in a line, you know who to pick. But if they are running around the playground
randomly, you have no idea who "the first 5" are, and you might pick a different person every time.

**❌ The Crime:**

```csharp
// Randomly skips 10 rows. The result is unpredictable.
var page2 = db.Users.Skip(10).Take(10).ToList();

// Which user is "Last"? Random.
var last = db.Users.Last();

// Chunks of 10 users. But who is in the first chunk? Random.
var chunks = db.Users.Chunk(10).ToList();
```

**✅ The Fix:**
Explicitly sort the data first.

```csharp
// Defined order: Sort by ID, then skip.
var page2 = db.Users.OrderBy(u => u.Id).Skip(10).Take(10).ToList();

// Defined order: Sort by Date, then get last.
var last = db.Users.OrderBy(u => u.CreatedAt).Last();

// Defined order: Sort by Name, then chunk.
var chunks = db.Users.OrderBy(u => u.Name).Chunk(10).ToList();
```

---

### LC016: Avoid DateTime.Now in Queries

Using `DateTime.Now` (or `UtcNow`) inside a LINQ query prevents the database execution plan from being cached
efficiently because the constant value changes every millisecond. It also makes unit testing impossible without mocking
the system clock.

**👶 Explain it like I'm a ten year old:** Imagine baking a cake. If the recipe says "Bake for 30 minutes," you can use
it every day. But if the recipe says "Bake until the clock shows exactly 4:03 PM on Tuesday," you can only use it once,
and then you have to write a new recipe.

**❌ The Crime:**

```csharp
// The value of DateTime.Now is baked into the SQL as a constant.
// This constant changes every time, forcing a new query plan.
var query = db.Users.Where(u => u.Dob < DateTime.Now);
```

**✅ The Fix:**
Store the date in a variable before the query.

```csharp
// The variable is passed as a parameter (@p0). The plan is cached.
var now = DateTime.Now;
var query = db.Users.Where(u => u.Dob < now);
```

---

### LC017: Whole Entity Projection

Loading entire entities when you only need a few properties wastes bandwidth, memory, and CPU. For large entities with
10+ properties, this can result in 10-40x more data transfer than necessary.

**👶 Explain it like I'm a ten year old:** Imagine you want to know your friend's phone number. Instead of asking for
just the number, you ask them to recite their entire autobiography—name, address, favorite foods, every vacation
they've ever taken. You only needed one fact, but you got a whole book.

**❌ The Crime:**

```csharp
// Loads all 12 columns for every product, but only uses Name
var products = db.Products.Where(p => p.Price > 100).ToList();
foreach (var p in products)
{
    Console.WriteLine(p.Name);  // Only Name is ever accessed!
}
```

**✅ The Fix:**
Use `.Select()` to project only what you need.

```csharp
// Projects only the Name column
var names = db.Products
    .Where(p => p.Price > 100)
    .Select(p => p.Name)
    .ToList();
```

---

### LC018: Avoid FromSqlRaw with Interpolation

Using interpolated strings (`$"{var}"`) with `FromSqlRaw` is a major security risk. It embeds variables directly into
the SQL string, bypassing parameterization and opening your database to SQL injection attacks.

**👶 Explain it like I'm a ten year old:** Imagine a bank where you write your name on a slip to get money. If you use
a special pen that lets you erase "Name: John" and write "Give John everything in the vault," you've just robbed the
bank. `FromSqlRaw` with interpolated strings is like using that eraseable pen.

**❌ The Crime:**

```csharp
// Potential SQL Injection!
var users = db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Name = '{name}'").ToList();
```

**✅ The Fix:**
Use `FromSqlInterpolated` or `FromSql` (EF Core 7+), which automatically parameterize the string.

```csharp
// Safe: EF Core handles parameterization
var users = db.Users.FromSqlInterpolated($"SELECT * FROM Users WHERE Name = {name}").ToList();
```

---

### LC020: StringComparison Smuggler

Using `string.Contains`, `StartsWith`, or `EndsWith` with a `StringComparison` argument in a LINQ query often cannot be
translated to SQL. This forces EF Core to pull all records from the database and filter them in your app's memory.

**👶 Explain it like I'm a ten year old:** Imagine asking a robot to find all red blocks in a giant bin. But you give
the robot a very complicated rule: "Find blocks that are red, but only if they are the exact shade of 'Sunset Crimson'
from a specific 1990s crayon box." The robot gets confused and just hands you the *entire* bin to sort yourself.

**❌ The Crime:**

```csharp
// Likely to cause client-side evaluation
var users = db.Users.Where(u => u.Name.Contains("admin", StringComparison.OrdinalIgnoreCase)).ToList();
```

**✅ The Fix:**
Use the simple overload. Databases are usually configured with a specific case-sensitivity (collation) anyway.

```csharp
// Translates to SQL LIKE
var users = db.Users.Where(u => u.Name.Contains("admin")).ToList();
```

---

### LC021: Avoid IgnoreQueryFilters

Global query filters are often used for critical security and logic, like multi-tenancy or soft-deletes. Using
`IgnoreQueryFilters()` bypasses these protections and can lead to data leaks.

**👶 Explain it like I'm a ten year old:** Imagine a high-security building where every door has a lock. `IgnoreQueryFilters`
is like a skeleton key that opens every single door at once. It's powerful, but if you use it by accident, you might
end up somewhere you're not supposed to be.

**❌ The Crime:**

```csharp
// Might accidentally see data from other tenants or deleted items
var allUsers = db.Users.IgnoreQueryFilters().ToList();
```

**✅ The Fix:**
Ensure that bypassing global filters is intentional and necessary for the specific task (e.g., admin tools).

---

### LC022: Explicit Loading N+1

Calling `.Load()` or `.Collection().Load()` inside a loop triggers a new database round-trip for every single item.
This is a classic performance killer that slows down as your data grows.

**👶 Explain it like I'm a ten year old:** Imagine you are serving 50 people dinner. Instead of bringing all the plates
out on a tray (Eager Loading), you walk to the kitchen, pick up ONE fork, walk back, give it to one person, and repeat
this 50 times. You'll be exhausted, and everyone will be hungry!

**❌ The Crime:**

```csharp
var users = db.Users.ToList();
foreach (var user in users)
{
    // Hits the database once for EVERY user in the list
    db.Entry(user).Collection(u => u.Orders).Load();
}
```

**✅ The Fix:**
Use `.Include()` to fetch the related data in the original query.

```csharp
// Fetches users and their orders in one (or few) efficient queries
var usersWithOrders = db.Users.Include(u => u.Orders).ToList();
```

---

### LC023: Suggest Find/FindAsync

`FirstOrDefault(x => x.Id == id)` always goes to the database. `Find(id)` first checks if the entity is already loaded
in your app's memory (the Change Tracker), which is much faster.

**👶 Explain it like I'm a ten year old:** Imagine you want to know if you have a blue shirt. `FirstOrDefault` is like
driving to the store to buy a new one just to check. `Find` is like looking in your closet first. If it's in the closet,
you save a trip!

**❌ The Crime:**

```csharp
// Always hits the database
var user = db.Users.FirstOrDefault(u => u.Id == userId);
```

**✅ The Fix:**
Use `Find` or `FindAsync` for primary key lookups.

```csharp
// Checks local memory first, then DB if not found
var user = db.Users.Find(userId);
```

---

### LC025: AsNoTracking with Update/Remove

`AsNoTracking()` is great for speed, but it tells EF Core "I'm only reading this." If you then try to `Update()` or
`Remove()` that entity, EF Core has to "guess" what changed, which leads to inefficient SQL that updates every single
column.

**👶 Explain it like I'm a ten year old:** Imagine you check out a library book but tell the librarian, "I'm just going
to look at the pictures." Then you go home and rewrite three chapters. When you return it, the librarian has to re-read
the *entire* book to figure out what you changed.

**❌ The Crime:**

```csharp
// Fetches as read-only
var user = db.Users.AsNoTracking().First(u => u.Id == id);
user.Name = "New Name";
// EF Core has to update ALL columns because it wasn't tracking changes
db.Users.Update(user);
```

**✅ The Fix:**
Remove `AsNoTracking()` if you plan to modify the entity.

```csharp
// Tracked by default
var user = db.Users.First(u => u.Id == id);
user.Name = "New Name";
// EF Core knows exactly which column changed
db.SaveChanges();
```

---

## ⚙️ Configuration

You can configure the severity of these rules in your `.editorconfig` file:

```ini
[*.cs]
dotnet_diagnostic.LC001.severity = error
dotnet_diagnostic.LC002.severity = error
dotnet_diagnostic.LC003.severity = warning
```

## 🤝 Contributing

Found a new way to smuggle bad queries? [Open an issue](https://github.com/georgepwall1991/LinqContraband/issues) or
submit a
PR!

License: [MIT](LICENSE)
