# Spec: LC020 - Avoid StringComparison overloads in query expressions

## Goal
Detect `string.Contains(...)`, `StartsWith(...)`, and `EndsWith(...)` overloads that pass `StringComparison` inside `System.Linq.Queryable` expression lambdas. These overloads often cannot be translated by EF Core providers, or they translate with provider-specific semantics that do not match the requested .NET comparison.

## The Problem
EF Core's SQL translation logic is optimized for simple string predicate overloads such as `Contains(string)`, `StartsWith(string)`, and `EndsWith(string)`. Passing `StringComparison` asks for .NET comparison semantics that many database providers cannot express directly. Depending on the provider and EF Core version, the query may fail translation or use semantics that are not what the application intended.

### Example Violation
```csharp
// Violation: likely to fail translation or depend on provider-specific behavior
var users = db.Users.Where(u => u.Name.Contains("admin", StringComparison.OrdinalIgnoreCase)).ToList();
```

### The Fix
Use the simple overload when the configured database collation already gives the desired behavior. When the comparison semantics are intentional, prefer a database collation, normalized search column, or provider-specific function that keeps the behavior explicit and server-side.

```csharp
// Correct when the database collation supplies the desired comparison semantics
var users = db.Users.Where(u => u.Name.Contains("admin")).ToList();
```

```csharp
// Also correct: comparison happens on a captured local before query translation
var needle = "admin";
var users = db.Users.Where(u => needle.Contains("a", StringComparison.OrdinalIgnoreCase));
```

## Analyzer Logic

### ID: `LC020`
### Category: `Performance`
### Severity: `Warning`

### Algorithm
1. **Target methods**: inspect `string.Contains`, `string.StartsWith`, and `string.EndsWith`.
2. **Overload check**: require an argument or bound parameter of type `System.StringComparison`.
3. **Queryable context check**: require an enclosing lambda passed to a `System.Linq.Queryable` invocation over an `IQueryable` source.
4. **Parameter-dependency check**: require the string receiver to depend on a query lambda parameter, such as `u.Name.Contains(...)` or a nested collection predicate like `u.Orders.Any(o => o.Number.Contains(...))`. Nested local enumerable predicates, captured locals, constants, and other client-side strings are ignored.

### Exceptions
- Calls on in-memory strings or `IEnumerable`.
- Calls on captured locals or constants inside a query predicate.
- Calls inside nested local enumerable predicates that do not depend on the query parameter.
- Calls inside custom `IQueryable` helpers that take delegate predicates instead of `Queryable` expression lambdas.

## Test Cases

### Violations
```csharp
db.Users.Where(x => x.Name.Contains("abc", StringComparison.OrdinalIgnoreCase));
db.Users.Any(x => x.Email.StartsWith("test", StringComparison.CurrentCulture));
db.Users.Where(x => x.Name.EndsWith(".org", StringComparison.OrdinalIgnoreCase));
db.Users.Where(x => x.Orders.Any(o => o.Number.Contains("rush", StringComparison.OrdinalIgnoreCase)));
```

### Valid
```csharp
db.Users.Where(x => x.Name.Contains("abc"));
"some string".Contains("abc", StringComparison.OrdinalIgnoreCase); // Not in IQueryable context

var search = "abc";
db.Users.Where(x => search.Contains("a", StringComparison.OrdinalIgnoreCase)); // Not query-parameter dependent

var tags = new List<string> { "admin" };
db.Users.Where(x => tags.Any(tag => tag.Contains("a", StringComparison.OrdinalIgnoreCase))); // Local predicate
```

## Shipped Behavior

LC020 reports query-parameter-dependent `Contains`, `StartsWith`, and `EndsWith` overloads that use `StringComparison` inside `System.Linq.Queryable` expression lambdas. The fixer removes the semantically bound `StringComparison` argument for straightforward calls, preserving the provider-side string predicate shape. It intentionally does not rewrite to `ToLower`, `ToUpper`, or provider-specific collation APIs because those choices are database- and domain-specific.
