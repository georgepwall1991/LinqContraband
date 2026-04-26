# Spec: LC040 - Mixed Tracking and No-Tracking

## Goal
Detect methods that mix tracked and no-tracking materialization from the same `DbContext`.

## The Problem
Switching between tracked and `AsNoTracking()` queries in one scope is easy to miss and can make later update behavior inconsistent.

### Example Violation
```csharp
var trackedUsers = db.Users.ToList();
var noTrackingUsers = db.Users.AsNoTracking().ToList();
```

## Analyzer Logic

### ID: `LC040`
### Category: `Reliability`
### Severity: `Info`

### Notes
This advisory reports only when the query provenance and materialization mode are both provable.

Only EF Core `EntityFrameworkQueryableExtensions.AsNoTracking`, `AsNoTrackingWithIdentityResolution`, and `AsTracking` calls are treated as tracking-mode markers. Custom extension methods with the same names are followed as ordinary query-chain calls and do not create mixed-mode evidence by themselves.
