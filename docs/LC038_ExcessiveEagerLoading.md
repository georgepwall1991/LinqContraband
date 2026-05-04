# Spec: LC038 - Excessive Eager Loading

## Goal
Detect query chains with too many `Include(...)` / `ThenInclude(...)` calls.

## The Problem
Very deep eager-loading chains often over-fetch related graphs, create large result shapes, and make query behavior harder to reason about.

### Example Violation
```csharp
var customers = db.Customers
    .Include(c => c.Address)
    .ThenInclude(a => a.Country)
    .ThenInclude(c => c.Region)
    .ThenInclude(r => r.Continent)
    .ToList();
```

## Analyzer Logic

### ID: `LC038`
### Category: `Performance`
### Severity: `Info`

### Configuration
```ini
dotnet_code_quality.LC038.include_threshold = 4
```

### Notes
The rule reports only when the include chain is provably EF-backed and the counted include steps meet or exceed the configured threshold.

LC038 follows common transparent query-shaping calls before the include chain, including `Where`, ordering, `Skip`, `Take`, `AsNoTracking`, `AsSplitQuery`, `AsSingleQuery`, and `TagWith`. This keeps filtered or tagged EF queries covered without guessing through arbitrary helper methods or projected shapes.
