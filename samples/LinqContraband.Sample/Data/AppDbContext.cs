using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Data;

/// <summary>
///     Sample DbContext used to demonstrate LinqContraband analyzers.
///     Contains various entity configurations to test both valid patterns and violations.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    ///     Standard user entity for testing query patterns.
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("SampleDb");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Fluent API Key for ValidFluentEntity
        modelBuilder.Entity<ValidFluentEntity>().HasKey(e => e.CodeKey);

        // Apply separate configuration
        modelBuilder.ApplyConfiguration(new ConfigurationEntityConfiguration());
    }

    #region LC017 - Whole Entity Projection Test Cases

    /// <summary>
    ///     Large entity with 12 properties for testing whole entity projection detection.
    /// </summary>
    public DbSet<LargeEntity> LargeEntities { get; set; } = null!;

    #endregion

    #region LC011 - Entity Missing Primary Key Test Cases

    /// <summary>
    ///     VIOLATION: This entity has no defined Primary Key.
    ///     Should trigger LC011.
    /// </summary>
    public DbSet<Product> Products { get; set; } = null!;

    /// <summary>
    ///     VALID: Primary Key defined by 'Id' convention.
    /// </summary>
    public DbSet<ValidIdEntity> ValidIds { get; set; } = null!;

    /// <summary>
    ///     VALID: Primary Key defined by 'ClassNameId' convention.
    /// </summary>
    public DbSet<ValidClassIdEntity> ValidClassIds { get; set; } = null!;

    /// <summary>
    ///     VALID: Primary Key defined by [Key] attribute.
    /// </summary>
    public DbSet<ValidKeyAttributeEntity> ValidKeyAttributes { get; set; } = null!;

    /// <summary>
    ///     VALID: Primary Key defined via Fluent API in OnModelCreating.
    /// </summary>
    public DbSet<ValidFluentEntity> ValidFluents { get; set; } = null!;

    /// <summary>
    ///     VALID: Primary Key defined in IEntityTypeConfiguration.
    /// </summary>
    public DbSet<ConfigurationEntity> ConfigurationEntities { get; set; } = null!;

    #endregion
}

#region Entity Definitions

public class User
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; init; }
    public List<Order> Orders { get; init; } = [];
    public List<Role> Roles { get; init; } = [];
    public List<ConfigurationEntity> Configurations { get; init; } = [];
}

public class Order
{
    public int Id { get; set; }
}

public class Role
{
    public int Id { get; set; }
}

// --- LC011 Test Entities ---

/// <summary>
///     Entity missing any form of Primary Key.
/// </summary>
public class Product
{
    public string Name { get; set; } = string.Empty;
}

public class ValidIdEntity
{
    public int Id { get; set; }
}

public class ValidClassIdEntity
{
    public int ValidClassIdEntityId { get; set; }
}

public class ValidKeyAttributeEntity
{
    [Key] public int CustomKey { get; set; }
}

public class ValidFluentEntity
{
    public int CodeKey { get; set; }
}

// --- LC017 Test Entity ---

/// <summary>
///     Large entity with 12 properties for testing whole entity projection detection.
/// </summary>
public class LargeEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

#endregion
