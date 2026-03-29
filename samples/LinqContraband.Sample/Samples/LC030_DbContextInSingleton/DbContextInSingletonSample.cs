using LinqContraband.Sample.Data;

namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}

namespace LinqContraband.Sample.Samples.LC030_DbContextInSingleton
{
    public class DbContextInSingletonSample : Microsoft.Extensions.Hosting.IHostedService
    {
        // VIOLATION: Holding a DbContext in a hosted service field can create a lifetime mismatch.
        private readonly AppDbContext _db;

        public DbContextInSingletonSample(AppDbContext db)
        {
            _db = db;
        }

        public void Run()
        {
            Console.WriteLine("Testing LC030...");
            var count = _db.Users.Count();
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
