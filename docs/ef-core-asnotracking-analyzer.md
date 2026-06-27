---
layout: default
title: EF Core AsNoTracking Analyzer
description: Use LinqContraband as an EF Core AsNoTracking analyzer for missing no-tracking reads, unsafe no-tracking writes, mixed tracking modes, and silent SaveChanges failures.
permalink: /ef-core-asnotracking-analyzer/
body_class: page-asnotracking-analyzer
---

# EF Core AsNoTracking Analyzer

LinqContraband is an EF Core `AsNoTracking` analyzer for .NET projects that want tracking-mode problems to show up in
the IDE and CI. It helps teams keep read-only queries lightweight without accidentally turning write paths into silent
no-ops.

Install the official analyzer package:

```bash
dotnet add package LinqContraband
```

## Why Tracking Mode Needs Review

EF Core tracks entities by default so it can detect changes before `SaveChanges`. That is useful for write workflows,
but wasteful on read-only screens, dashboards, search pages, and API responses.

The awkward part is that `AsNoTracking()` is not just a performance switch. Once a query is no-tracking, mutating the
entity later does not update the change tracker. A method can compile, run, call `SaveChanges`, and still persist
nothing.

```csharp
var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);

user.Name = "New Name";
db.SaveChanges(); // No tracked change is saved.
```

## LinqContraband Rules That Help

| Rule | What it detects | Review direction |
| --- | --- | --- |
| [LC009: missing AsNoTracking](/LinqContraband/LC009_MissingAsNoTracking.html) | Read-only materialization from an EF source without an explicit tracking mode. | Add `AsNoTracking()` or document why the result must be tracked. |
| [LC025: AsNoTracking with Update/Remove](/LinqContraband/LC025_AsNoTrackingWithUpdate.html) | An entity from a no-tracking query is later passed into `Update`, `Remove`, range variants, or explicit state changes. | Use a tracked query for write paths or change the update strategy intentionally. |
| [LC040: mixed tracking modes](/LinqContraband/LC040_MixedTrackingAndNoTracking.html) | A method materializes tracked and no-tracking results from the same DbContext. | Split read-only and write workflows or make the mixed-mode intent obvious. |
| [LC044: no-tracking entity mutated before SaveChanges](/LinqContraband/LC044_AsNoTrackingThenModifySilentWrite.html) | An entity loaded with `AsNoTracking()` is mutated before `SaveChanges` without re-attach. | Remove `AsNoTracking()`, call `Update()`, or set `Entry(entity).State` to `Modified` before saving. |

## Safer Patterns

Use no-tracking explicitly for pure reads:

```csharp
var users = await db.Users
    .AsNoTracking()
    .Where(user => user.IsActive)
    .Select(user => new UserListItem(user.Id, user.Name))
    .ToListAsync();
```

Keep tracked queries for entity edits:

```csharp
var user = await db.Users.FirstOrDefaultAsync(user => user.Id == id);

if (user is not null)
{
    user.Name = name;
    await db.SaveChangesAsync();
}
```

Do not use `AsNoTracking()` as the local source for an entity that the same method immediately updates or removes.
LinqContraband reports that shape through LC025 because it is usually a read-only marker leaking into a write path. When
a genuinely detached write comes from outside the current read method, keep the attach/update path isolated and reviewed
instead of treating no-tracking queries as a general update pattern.

## CI Severity Starter

Start with warnings for behaviour-changing tracking rules, then promote only the rules that match team policy:

```ini
[*.cs]

# Read-only query performance
dotnet_diagnostic.LC009.severity = suggestion

# Tracking correctness and silent-write risks
dotnet_diagnostic.LC025.severity = warning
dotnet_diagnostic.LC040.severity = warning
dotnet_diagnostic.LC044.severity = error
```

Use the [EF Core query analyzer CI guide](/LinqContraband/ef-core-query-analyzer-ci/) when these diagnostics should run
on every pull request. Use the [EF Core query performance checklist](/LinqContraband/ef-core-query-performance-checklist/)
when reviewers need the broader query-performance context.

## Official Links

- Canonical repository: [github.com/georgepwall1991/LinqContraband](https://github.com/georgepwall1991/LinqContraband)
- Official NuGet package: [nuget.org/packages/LinqContraband](https://www.nuget.org/packages/LinqContraband)
- Full rule catalog: [georgepwall1991.github.io/LinqContraband/rule-catalog.html](https://georgepwall1991.github.io/LinqContraband/rule-catalog.html)
- Safe install guidance: [Official LinqContraband downloads and authenticity](/LinqContraband/security-and-authenticity/)
