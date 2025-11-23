using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // Needed for async
using Microsoft.EntityFrameworkCore; // Use our Mock

namespace LinqContraband.Sample
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public List<Order> Orders { get; set; }
        public List<Role> Roles { get; set; }
    }

    public class Order { public int Id { get; set; } }
    public class Role { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseInMemoryDatabase("SampleDb");
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var users = new List<User>
            {
                new User { Id = Guid.NewGuid(), Name = "Alice", Age = 30, Orders = new(), Roles = new() },
                new User { Id = Guid.NewGuid(), Name = "Bob", Age = 25, Orders = new(), Roles = new() }
            }.AsQueryable();

            // LC001: Local Method
            // This calls a local method inside an IQueryable expression, preventing SQL translation.
            Console.WriteLine("Testing LC001...");
            var localResult = users.Where(u => IsAdult(u.Age)).ToList();

            // LC002: Premature Materialization
            // This calls ToList() (materializing all records) before filtering with Where().
            Console.WriteLine("Testing LC002...");
            var prematureResult = users.ToList().Where(u => u.Age > 20).ToList();

            // LC003: Any Over Count
            // This uses Count() > 0 to check existence, which may iterate the whole table.
            Console.WriteLine("Testing LC003...");
            if (users.Count() > 0)
            {
                Console.WriteLine("Users exist");
            }

            // LC004: Guid In Query
            // Common mistake 1: "Random Sort" - forcing a shuffle by ordering by a new Guid.
            Console.WriteLine("Testing LC004 (Random Sort)...");
            var randomSortResult = users.OrderBy(u => Guid.NewGuid()).ToList();

            // Common mistake 2: Generating IDs in projection
            Console.WriteLine("Testing LC004 (Projection)...");
            var projectionResult = users.Select(u => new { NewId = Guid.NewGuid(), u.Name }).ToList();

            // LC005: Multiple OrderBy
            // This calls OrderBy twice, resetting the first sort instead of chaining with ThenBy.
            Console.WriteLine("Testing LC005...");
            var orderResult = users.OrderBy(u => u.Age).OrderBy(u => u.Name).ToList();

            // LC006: Cartesian Explosion
            // This includes multiple collections in a single query without splitting.
            Console.WriteLine("Testing LC006...");
            var cartesianResult = users.Include(u => u.Orders).Include(u => u.Roles).ToList();

            // LC007: N+1 Looper
            // This executes a database query inside a loop.
            Console.WriteLine("Testing LC007...");
            var targetIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            foreach (var id in targetIds)
            {
                var user = users.Where(u => u.Id == id).ToList();
            }

            // LC008: Sync Blocker
            // This calls synchronous ToList inside an async method.
            Console.WriteLine("Testing LC008...");
            var syncBlocker = users.ToList(); // Should trigger LC008 because Main is async
            await Task.Delay(10); // Ensure async method

            // LC009: Missing AsNoTracking
            // This method returns entities but doesn't use AsNoTracking() and doesn't save changes.
            Console.WriteLine("Testing LC009...");
            GetUsersReadOnly(users);

            // LC010: SaveChanges in Loop
            // This calls SaveChanges() inside a loop, causing N+1 database transactions.
            Console.WriteLine("Testing LC010...");
            using var db = new AppDbContext();
            foreach (var user in users)
            {
                user.Name += " Updated";
                db.SaveChanges(); // Should trigger LC010
            }
        }

        // Local method for LC001
        static bool IsAdult(int age) => age >= 18;

        // LC009 Violation
        static List<User> GetUsersReadOnly(IQueryable<User> users)
        {
            // Violation: Returning entities from read-only context without AsNoTracking
            return users.Where(u => u.Age > 18).ToList();
        }
    }
}
