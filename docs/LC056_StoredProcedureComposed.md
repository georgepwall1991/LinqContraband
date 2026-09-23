---
layout: default
title: "LC056: LINQ composed over a stored procedure call"
description: "LC056 flags EF Core FromSql, FromSqlRaw and SqlQuery calls that run a stored procedure (EXEC) and are then composed with LINQ, which throws at run time."
---

# LC056: LINQ composed over a stored procedure call

## In Plain Terms

You asked the kitchen for the set menu, then told the waiter to swap out the dessert. The set menu can't be changed; you have to take it as it comes and rearrange the plate yourself.

## Goal

Detect raw SQL queries that call a stored procedure with `EXEC` or `EXECUTE` and then apply LINQ operators that EF Core has to translate into SQL.

## The Problem

When LINQ follows `FromSql`, `FromSqlRaw`, `FromSqlInterpolated`, `SqlQuery` or `SqlQueryRaw`, EF Core puts the raw SQL in a subquery and builds the rest of the query around it: `SELECT ... FROM (EXEC dbo.GetBlogs) AS b WHERE ...`. A stored procedure call cannot be a subquery. EF Core checks this before sending the command and throws `InvalidOperationException`: "'FromSql' or 'SqlQuery' was called with non-composable SQL and with a query composing over it". The code compiles and only fails when it runs.

```csharp
// Violation: Where, Include, FirstOrDefault, CountAsync and friends all compose over the EXEC.
var blogs = await db.Blogs
    .FromSql($"EXEC dbo.GetBlogs {tenantId}")
    .Where(b => b.Rating > 3)
    .ToListAsync(ct);
```

## The Fix

Run the procedure as it is and apply the rest in memory:

```csharp
var blogs = db.Blogs
    .FromSql($"EXEC dbo.GetBlogs {tenantId}")
    .AsEnumerable()
    .Where(b => b.Rating > 3)
    .ToList();
```

Use `AsAsyncEnumerable()` for an asynchronous pipeline. If the filter matters for performance, move it into the procedure as a parameter, or use a table-valued function (`SELECT * FROM dbo.GetBlogs(@tenantId)`), which EF Core can compose over.

## Analyzer Logic

### ID: `LC056`
### Category: `Correctness`
### Severity: `Warning`

1. Find a LINQ operator that EF Core translates around its source: any `Queryable` operator except `AsQueryable`, `Cast`, and an identity `Select(x => x)`, plus EF Core's `Include`, `ThenInclude`, async element and aggregate operators (`FirstOrDefaultAsync`, `CountAsync`, `AnyAsync`, ...), and `ExecuteDelete` / `ExecuteUpdate`.
2. Walk back through operators that leave the SQL unchanged: `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `TagWith`, `TagWithCallSite`, `AsSplitQuery`, `AsSingleQuery`, and `AsQueryable`.
3. Report when the walk reaches `FromSql`, `FromSqlRaw`, `FromSqlInterpolated`, `Database.SqlQuery` or `Database.SqlQueryRaw` whose SQL, a constant or the text before the first interpolation hole, starts with `EXEC` or `EXECUTE` after whitespace and SQL comments. EF Core skips the same whitespace and comments before its own check.

## When it stays quiet (non-goals)

- Operators that run the SQL unchanged: `ToList`, `ToListAsync`, `ToArray`, `AsEnumerable`, `AsAsyncEnumerable`, `foreach`, `Load`, and anything after them.
- SQL that starts with `SELECT` or `WITH`, or with another word such as `EXECUTIONS_VIEW`.
- SQL whose start is not known at compile time: a variable, or an interpolation hole at the start.
- Raw SQL queries stored in a local and composed later, or passed through a project helper method. Only fluent chains are followed.
- Other statements EF Core cannot compose over, such as `CALL` or `DECLARE`. Only `EXEC` and `EXECUTE` are recognized.

## Code Fix

Inserts `.AsEnumerable()` just before the composing operator, after any pass-through calls such as `AsNoTracking()`. When only `.AsAsyncEnumerable()` compiles, for example before an async terminal with an async LINQ library available, it inserts that instead. The fixer compiles the result and offers no fix when the rewrite would add an error: `Include` needs the query, and so does a result stored as `IQueryable<T>` or later passed to `ToListAsync`.

## Test Cases

### Violations

```csharp
db.Blogs.FromSqlRaw("EXEC dbo.GetBlogs").Where(b => b.Rating > 3).ToList();
db.Blogs.FromSqlRaw("EXEC dbo.GetBlog @id", id).AsNoTracking().FirstOrDefault();
db.Database.SqlQuery<int>($"EXEC dbo.GetIds {tenantId}").Any();
```

### Valid

```csharp
db.Blogs.FromSqlRaw("EXEC dbo.GetBlogs").ToList();
db.Blogs.FromSqlRaw("EXEC dbo.GetBlogs").AsEnumerable().Where(b => b.Rating > 3).ToList();
db.Blogs.FromSqlRaw("SELECT * FROM Blogs").Where(b => b.Rating > 3).ToList();
```
