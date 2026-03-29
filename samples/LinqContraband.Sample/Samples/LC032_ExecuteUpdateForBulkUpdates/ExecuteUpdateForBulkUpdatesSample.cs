using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    internal static class SampleExecuteUpdateSupport
    {
        public static int ExecuteUpdate<TSource>(this IQueryable<TSource> source, object? updates = null) => 0;

        public static Task<int> ExecuteUpdateAsync<TSource>(this IQueryable<TSource> source, object? updates = null) => Task.FromResult(0);
    }
}

namespace LinqContraband.Sample.Samples.LC032_ExecuteUpdateForBulkUpdates
{
    /// <summary>
    ///     Demonstrates the "Use ExecuteUpdate for Provable Bulk Scalar Updates" advisory (LC032).
    /// </summary>
    public static class ExecuteUpdateForBulkUpdatesSample
    {
        public static void Run()
        {
            Console.WriteLine("Testing LC032...");

            using var db = new BulkAppDbContext();

            // ADVISORY: This tracked bulk update can usually become a single ExecuteUpdate() call.
            foreach (var user in db.Users.Where(u => u.IsActive))
            {
                user.Name = "Archived";
            }

            db.SaveChanges();
        }
    }

    internal sealed class BulkUser
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class BulkAppDbContext : DbContext
    {
        public DbSet<BulkUser> Users { get; set; } = null!;
    }
}
