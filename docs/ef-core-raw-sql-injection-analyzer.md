---
layout: default
title: EF Core Raw SQL Injection Analyzer
description: Use LinqContraband as an EF Core raw SQL injection analyzer that flags interpolated SQL, constructed SQL strings, and unsafe query-filter bypasses at compile time.
permalink: /ef-core-raw-sql-injection-analyzer/
body_class: page-raw-sql-analyzer
---

# EF Core Raw SQL Injection Analyzer

LinqContraband is a compile-time EF Core raw SQL injection analyzer for .NET teams that want unsafe SQL construction to
fail in review and CI instead of waiting for a security incident. It ships as a Roslyn analyzer package and flags risky
raw SQL patterns while developers are writing Entity Framework Core code.

Install the official NuGet package:

```bash
dotnet add package LinqContraband
```

## Why Raw SQL Needs Static Analysis

EF Core provides safe APIs for interpolated SQL, but it also exposes raw-string APIs for cases where teams need full
control. Those raw APIs are easy to misuse when a query starts as a string literal and later grows user input, string
concatenation, or interpolation.

```csharp
var users = db.Users.FromSqlRaw($"SELECT * FROM Users WHERE Name = '{name}'");
```

That code compiles. The dangerous part is not obvious at the call site unless the reviewer knows the exact EF Core API
families and the difference between raw SQL and parameterized interpolation.

## LinqContraband Rules That Help

| Rule | What it detects | Safer direction |
| --- | --- | --- |
| [LC018: interpolated FromSqlRaw and SqlQueryRaw](/LinqContraband/LC018_AvoidFromSqlRawWithInterpolation.html) | Interpolated or concatenated SQL passed to raw query APIs. | Prefer parameterized APIs such as `FromSql`/`FromSqlInterpolated` or explicit parameters. |
| [LC034: interpolated ExecuteSqlRaw](/LinqContraband/LC034_AvoidExecuteSqlRawWithInterpolation.html) | Interpolated or concatenated SQL passed to raw command APIs. | Prefer `ExecuteSql`/interpolated APIs or explicit `DbParameter` values. |
| [LC037: constructed raw SQL strings](/LinqContraband/LC037_RawSqlStringConstruction.html) | SQL strings built from interpolation, concatenation, `string.Format`, or similar construction before `FromSqlRaw`. | Keep SQL constant and parameterize values. |
| [LC021: IgnoreQueryFilters](/LinqContraband/LC021_AvoidIgnoreQueryFilters.html) | Query filter bypasses that can skip multi-tenant, soft-delete, or security filters. | Keep bypasses explicit, reviewed, and documented. |

## Safer EF Core Patterns

Prefer EF Core's parameterized interpolation APIs where possible:

```csharp
var users = db.Users.FromSql($"SELECT * FROM Users WHERE Name = {name}");
```

When a raw API is necessary, keep SQL text constant and pass values as parameters:

```csharp
var nameParameter = new SqlParameter("@name", name);

var users = db.Users.FromSqlRaw(
    "SELECT * FROM Users WHERE Name = @name",
    nameParameter);
```

LinqContraband does not try to replace a secure code review or threat model. It catches the common raw SQL footguns early
so reviewers can focus on the cases that genuinely need human judgement.

## Where It Fits

- Pull request checks for APIs, admin tools, and reporting code that use EF Core.
- Migration from ad hoc SQL strings toward parameterized EF Core APIs.
- Security reviews where raw SQL and query-filter bypasses need a searchable compile-time signal.
- Teams that want raw SQL safety guidance in Visual Studio, Rider, VS Code, and CI.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
