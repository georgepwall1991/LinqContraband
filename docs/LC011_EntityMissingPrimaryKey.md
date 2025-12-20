# Spec: LC011 - Entity Missing Primary Key

## Goal
Detect entity classes that do not have a primary key defined.

## The Problem
Entity Framework Core requires a primary key to track entity identity and perform updates/deletes. If an entity is missing a key, EF Core will throw a runtime error or prevent certain database operations.

### Example Violation
```csharp
// Violation: No ID or Key defined
public class Product
{
    public string Name { get; set; }
}
```

### The Fix
Define a primary key using the `Id` convention or the `[Key]` attribute.

```csharp
// Correct
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

## Analyzer Logic

### ID: `LC011`
### Category: `Reliability`
### Severity: `Warning`
