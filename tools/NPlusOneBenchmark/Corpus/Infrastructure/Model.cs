using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneBenchmark.Corpus;

public enum OrderStatus
{
    Open,
    Paid,
    Shipped,
}

public class Region
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();
}

public class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public bool IsActive { get; set; }

    public int RegionId { get; set; }

    public virtual Region Region { get; set; } = null!;

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public DateTime PlacedOn { get; set; }

    public OrderStatus Status { get; set; }

    public virtual ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();

    /// <summary>A member on the entity that reads a navigation.</summary>
    public string CustomerLabel => $"{Id} for {Customer.Name}";
}

public class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public virtual Order Order { get; set; } = null!;

    public int ProductId { get; set; }

    public virtual Product Product { get; set; } = null!;

    public int Quantity { get; set; }
}

public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int PriceInCents { get; set; }

    public int Stock { get; set; }

    public int CategoryId { get; set; }

    public virtual Category Category { get; set; } = null!;
}

public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}

public class AuditEntry
{
    public int Id { get; set; }

    public string Message { get; set; } = "";
}

/// <summary>The context most cases use. Navigations load only when asked for.</summary>
public class ShopDb : DbContext
{
    private readonly DbConnection _connection;
    private readonly SqlCounter _counter;

    public ShopDb(DbConnection connection, SqlCounter counter)
    {
        _connection = connection;
        _counter = counter;
    }

    public DbSet<Region> Regions => Set<Region>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite(_connection).AddInterceptors(_counter);
}

/// <summary>The same model with lazy-loading proxies: reading an unloaded navigation runs a query.</summary>
public class LazyShopDb : ShopDb
{
    public LazyShopDb(DbConnection connection, SqlCounter counter)
        : base(connection, counter)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseLazyLoadingProxies();
    }
}

public static class Seed
{
    public const int RegionCount = 3;
    public const int CategoryCount = 3;

    /// <summary>
    /// Fills the database. Customers, orders, order lines and products scale with <paramref name="size"/>;
    /// regions and categories are fixed lookup tables.
    /// </summary>
    public static void Fill(ShopDb db, int size)
    {
        for (var i = 1; i <= RegionCount; i++)
            db.Regions.Add(new Region { Id = i, Name = $"Region {i}" });

        for (var i = 1; i <= CategoryCount; i++)
            db.Categories.Add(new Category { Id = i, Name = $"Category {i}" });

        var productCount = size + 2;
        for (var i = 1; i <= productCount; i++)
        {
            db.Products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                PriceInCents = 100 * i,
                Stock = 10,
                CategoryId = (i % CategoryCount) + 1,
            });
        }

        var orderId = 0;
        var lineId = 0;
        for (var i = 1; i <= size; i++)
        {
            db.Customers.Add(new Customer
            {
                Id = i,
                Name = $"Customer {i}",
                IsActive = i % 2 == 1,
                RegionId = (i % RegionCount) + 1,
            });

            for (var o = 0; o < 2; o++)
            {
                orderId++;
                db.Orders.Add(new Order
                {
                    Id = orderId,
                    CustomerId = i,
                    PlacedOn = new DateTime(2026, 1, 1).AddDays(orderId),
                    Status = (OrderStatus)(orderId % 3),
                });

                for (var l = 0; l < 2; l++)
                {
                    lineId++;
                    db.OrderLines.Add(new OrderLine
                    {
                        Id = lineId,
                        OrderId = orderId,
                        ProductId = (lineId % productCount) + 1,
                        Quantity = l + 1,
                    });
                }
            }
        }

        db.SaveChanges();
    }
}
