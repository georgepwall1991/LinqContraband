# LinqContraband

**LinqContraband** is a Roslyn Analyzer for .NET that prevents common Entity Framework Core performance pitfalls by detecting client-side evaluation risks and premature query materialization.

## Rules

### LC001: The Local Method Smuggler
Detects usage of local methods inside `IQueryable` expressions that cannot be translated to SQL.

**The Crime:**
```csharp
// CalculateAge is a local C# method, EF Core cannot translate it to SQL.
var query = db.Users.Where(u => CalculateAge(u.Dob) > 18);
```

**The Fix:** Extract the logic or variable outside the query, or use a supported translation method.

### LC002: Premature Materialization
Detects when filtering logic (`Where`) is applied *after* materialization (`ToList`, `ToArray`), causing the entire table to be fetched into memory.

**The Crime:**
```csharp
// ToList() executes the query (SELECT * FROM Users), fetching ALL rows.
// Where() then filters in memory.
var query = db.Users.ToList().Where(u => u.Age > 18);
```

**The Fix:** Move filtering before materialization.
```csharp
// SELECT * FROM Users WHERE Age > 18
var query = db.Users.Where(u => u.Age > 18).ToList();
```

## Installation

Install via NuGet (Package generation coming soon).

## Development

### Requirements
- .NET 10.0 SDK (compatible with .NET 8/9 projects)

### Building and Testing
```bash
dotnet build
dotnet test
```


