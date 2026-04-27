# Spec: LC011 - Entity Missing Primary Key

## Goal

Detect `DbSet<TEntity>` entity types that EF Core would treat as regular tracked entities but that have no primary key definition.

## The Problem

EF Core needs a primary key for tracked entities so it can identify rows, perform updates/deletes, resolve relationships, and maintain identity in the change tracker. A missing key usually fails model validation at startup or leaves the entity usable only as a keyless query type when that was not intended.

## Example Violation

```csharp
public class AppDbContext : DbContext
{
    public DbSet<Product> Products { get; set; }
}

public class Product
{
    public string Name { get; set; }
}
```

## Supported Safe Shapes

LC011 treats these as valid primary-key or intentional opt-out patterns:

- Convention keys: public mapped `Id` or `{EntityName}Id` properties.
- Data annotations: public mapped properties with `System.ComponentModel.DataAnnotations.KeyAttribute`.
- EF Core composite keys: class-level `Microsoft.EntityFrameworkCore.PrimaryKeyAttribute` where all referenced properties exist, are public, mapped, and key-compatible.
- Fluent API keys in `OnModelCreating`: `modelBuilder.Entity<TEntity>().HasKey(...)`, scoped local builder variables, and chained builder calls such as `entity.ToTable("Products").HasKey(...)`.
- Applied configurations: inline or local `modelBuilder.ApplyConfiguration(...)` calls, and `ApplyConfigurationsFromAssembly(typeof(LocalType).Assembly)` when the marker type belongs to the current source assembly and the configuration's `Configure` method calls `HasKey(...)` or `HasNoKey()` for its own `EntityTypeBuilder<TEntity>`.
- Intentional keyless types: `Microsoft.EntityFrameworkCore.KeylessAttribute` or `HasNoKey()`.
- Owned types: `Microsoft.EntityFrameworkCore.OwnedAttribute`, generic ownership such as `OwnsOne<Address>(...)`, and inferred ownership such as `OwnsOne(e => e.Address)` / `OwnsMany(e => e.Items)`.

Attributes are namespace-checked. A custom class named `KeyAttribute`, `PrimaryKeyAttribute`, or `KeylessAttribute` does not suppress LC011.

## Fixes

Preferred fixes are domain-specific:

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

```csharp
modelBuilder.Entity<Product>().HasKey(p => p.ProductCode);
```

```csharp
modelBuilder.ApplyConfiguration(new ProductConfiguration());

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.ProductCode);
    }
}
```

The code fix offers `public int Id { get; set; }` only when the entity is source-editable and has no existing `Id` member. It intentionally avoids generating a duplicate `Id` when the current member is private, ignored, or a navigation property; choose the correct domain key manually in those cases.

## Analyzer Logic

### ID: `LC011`
### Category: `Design`
### Severity: `Warning`

LC011 starts from `DbSet<TEntity>` members on source `DbContext` types, checks the entity's inheritance chain for valid mapped key properties, then folds in keyless/owned/fluent configuration discovered from the analyzed context. Standalone `IEntityTypeConfiguration<TEntity>` classes are not trusted unless the context applies them, preventing one context's configuration from suppressing diagnostics in another context.
