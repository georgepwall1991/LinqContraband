using System;
using System.Collections.Generic;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC010_SaveChangesInLoop
{
    public class SaveChangesInLoopSample
    {
        public static void Run(IEnumerable<User> users)
        {
            Console.WriteLine("Testing LC010...");
            using var db = new AppDbContext();
            // This calls SaveChanges() inside a loop, causing N+1 database transactions.
            foreach (var user in users)
            {
                user.Name += " Updated";
                db.SaveChanges(); 
            }
        }
    }
}

