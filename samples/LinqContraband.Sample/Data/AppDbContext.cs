using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LinqContraband.Sample.Data
{
    /// <summary>
    /// Sample DbContext used to demonstrate LinqContraband analyzers.
    /// Contains various entity configurations to test both valid patterns and violations.
    /// </summary>
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// Standard user entity for testing query patterns.
        /// </summary>
        public DbSet<User> Users { get; set; } = null!;

        #region LC011 - Entity Missing Primary Key Test Cases

        /// <summary>
        /// VIOLATION: This entity has no defined Primary Key.
        /// Should trigger LC011.
        /// </summary>
        public DbSet<Product> Products { get; set; } = null!;

        /// <summary>
        /// VALID: Primary Key defined by 'Id' convention.
        /// </summary>
        public DbSet<ValidIdEntity> ValidIds { get; set; } = null!;

        /// <summary>
        /// VALID: Primary Key defined by 'ClassNameId' convention.
        /// </summary>
        public DbSet<ValidClassIdEntity> ValidClassIds { get; set; } = null!;

        /// <summary>
        /// VALID: Primary Key defined by [Key] attribute.
        /// </summary>
        public DbSet<ValidKeyAttributeEntity> ValidKeyAttributes { get; set; } = null!;

        /// <summary>
        /// VALID: Primary Key defined via Fluent API in OnModelCreating.
        /// </summary>
        public DbSet<ValidFluentEntity> ValidFluents { get; set; } = null!;

        /// <summary>
        /// VALID: Primary Key defined in IEntityTypeConfiguration.
        /// </summary>
        public DbSet<ValidConfigurationEntity> ValidConfigurations { get; set; } = null!;

        #endregion

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseInMemoryDatabase("SampleDb");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure Fluent API Key for ValidFluentEntity
            modelBuilder.Entity<ValidFluentEntity>().HasKey(e => e.CodeKey);

            // Apply separate configuration
            modelBuilder.ApplyConfiguration(new ValidConfigurationEntityConfiguration());
        }
    }

    #region Entity Definitions

    public class User
    {
        public Guid Id { get; init; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; init; }
        public List<Order> Orders { get; init; } = [];
        public List<Role> Roles { get; init; } = [];
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
    /// Entity missing any form of Primary Key.
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
        [Key]
        public int CustomKey { get; set; }
    }

    public class ValidFluentEntity
    {
        public int CodeKey { get; set; }
    }

    #endregion
}
