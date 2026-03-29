# Spec: LC042 - Missing Query Tags

## Goal
Detect complex EF queries that lack `TagWith(...)` or `TagWithCallSite()`.

## The Problem
Complex query shapes are much easier to trace in logs, profilers, and query plans when they carry an explicit tag.

### Example Violation
```csharp
var users = db.Users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Take(10)
    .ToList();
```

## Analyzer Logic

### ID: `LC042`
### Category: `Performance`
### Severity: `Info`

### Configuration
```ini
dotnet_code_quality.LC042.query_operator_threshold = 3
```

### Notes
The rule reports only when the query is provably EF-backed, has no existing tag, and meets or exceeds the configured operator threshold.
