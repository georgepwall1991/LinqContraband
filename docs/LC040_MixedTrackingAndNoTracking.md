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

Straight-line local query aliases are resolved at the materialization point. A local reassigned from tracked to no-tracking queries on the same context can report, while a reassignment from a different context or inside conditional control flow stays quiet.

Mutually exclusive `if`/`else` branches and `switch` sections are not treated as mixed tracking evidence by themselves. Later materialization still compares against every reachable earlier tracking mode so split branches followed by shared work can be reported when one path really mixes modes.
