# LC017: Whole Entity Projection

## Overview

**Diagnostic ID**: LC017
**Severity**: Info
**Category**: Performance

## What It Detects

LC017 detects Entity Framework Core queries that load entire entities when only a small subset of properties are actually accessed. This is a common performance anti-pattern that wastes bandwidth, memory, and CPU.

## The Crime

```csharp
// Bad: Loads all 12 columns, but only uses Name
var products = context.Products.Where(p => p.Price > 100).ToList();
foreach (var p in products)
{
    Console.WriteLine(p.Name);  // Only Name is ever accessed!
}
```

When you query entities without a `.Select()` projection, EF Core retrieves ALL columns from the database. If your entity has many properties but you only use a few, you're:
- Transferring unnecessary data over the network
- Allocating memory for unused properties
- Potentially causing change tracking overhead for data you don't need

## The Fix

Use `.Select()` to project only the properties you need:

```csharp
// Good: Projects only the needed property
var names = context.Products
    .Where(p => p.Price > 100)
    .Select(p => p.Name)
    .ToList();

foreach (var name in names)
{
    Console.WriteLine(name);
}
```

Or project into a DTO:

```csharp
// Good: Project into a DTO with only needed fields
var products = context.Products
    .Where(p => p.Price > 100)
    .Select(p => new ProductSummary
    {
        Id = p.Id,
        Name = p.Name
    })
    .ToList();
```

## Why This Matters

### Performance Impact

Consider an entity with 12 properties, including a large `Description` field:

| Approach | Data Retrieved | Memory Used |
|----------|---------------|-------------|
| No projection | All 12 columns (~2KB per row) | ~2MB for 1000 rows |
| With `.Select(p => p.Name)` | 1 column (~50 bytes per row) | ~50KB for 1000 rows |

That's a **40x reduction** in data transfer and memory usage.

### Additional Benefits

1. **Faster queries**: SQL Server only reads necessary columns from disk/memory
2. **No change tracking**: Projections to non-entity types bypass EF's change tracker
3. **Cleaner code**: DTOs explicitly declare what data the code needs

## Conservative Detection

LC017 uses conservative detection to minimize false positives:

- **Only flags large entities**: Entities must have 10+ properties
- **Only flags clear waste**: Must access only 1-2 properties of the entity
- **Only flags local usage**: Skips when entities are returned from methods
- **Skips external method calls**: If entity is passed to another method, can't track usage
- **Skips lambdas**: If entity is used in a lambda/delegate, can't reliably track
- **Collection queries only**: Flags `ToList()`/`ToArray()`, not single-entity `First()`/`Single()`

## When LC017 Does NOT Trigger

1. **Already using projection**:
   ```csharp
   // OK: Already projected
   var names = context.Products.Select(p => p.Name).ToList();
   ```

2. **Small entities**:
   ```csharp
   // OK: Entity has only 3 properties - not worth flagging
   var users = context.SmallEntities.ToList();
   ```

3. **Entity is returned**:
   ```csharp
   // OK: Can't track how caller uses the entity
   public List<Product> GetProducts() => context.Products.ToList();
   ```

4. **Entity passed to method**:
   ```csharp
   // OK: Can't track usage in external method
   var products = context.Products.ToList();
   ProcessProducts(products);
   ```

5. **Most properties accessed**:
   ```csharp
   // OK: Accessing 7+ of 12 properties justifies full load
   foreach (var p in context.Products.ToList())
   {
       Console.WriteLine($"{p.Id} {p.Name} {p.Description} {p.Price}...");
   }
   ```

## Code Fix

LC017 provides an automatic code fix that adds a `.Select()` projection before the materializer. The fix:

1. **Analyzes property accesses**: Determines which properties of the entity are actually used in subsequent code
2. **Generates anonymous type projection**: Creates a `.Select(e => new { e.Prop1, e.Prop2 })` with only the accessed properties
3. **Sorts properties alphabetically**: Ensures consistent, predictable output

### Before Fix

```csharp
var entities = db.LargeEntities.ToList();
foreach (var e in entities)
{
    Console.WriteLine(e.Name);
}
```

### After Fix

```csharp
var entities = db.LargeEntities.Select(e => new { e.Name }).ToList();
foreach (var e in entities)
{
    Console.WriteLine(e.Name);
}
```

> **Note**: After applying the fix, you may need to adjust your code to work with the anonymous type instead of the full entity. Consider creating a named DTO class for better maintainability.

## Configuration

You can configure the severity in your `.editorconfig`:

```ini
# Make LC017 an error
dotnet_diagnostic.LC017.severity = error

# Disable LC017
dotnet_diagnostic.LC017.severity = none
```

## Related Analyzers

- **LC002**: Premature Materialization - Detects `ToList()` before filtering
- **LC009**: Missing AsNoTracking - Suggests `AsNoTracking()` for read-only queries
