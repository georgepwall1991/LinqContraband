using System;
using System.Linq;
using System.Threading.Tasks;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC008_SyncBlocker
{
    public class SyncBlockerSample
    {
        public static async Task RunAsync(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC008...");
            // This calls synchronous ToList inside an async method.
            var syncBlocker = users.ToList(); 
            await Task.Delay(10); // Ensure async context
        }
    }
}

