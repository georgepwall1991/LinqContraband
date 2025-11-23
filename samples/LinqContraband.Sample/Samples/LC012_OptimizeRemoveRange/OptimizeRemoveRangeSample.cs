using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC012_OptimizeRemoveRange
{
    public class OptimizeRemoveRangeSample
    {
        public static void Run()
        {
            Console.WriteLine("Testing LC012...");
            using var db = new AppDbContext();

            var usersToDelete = db.Users.Where(u => u.Id < Guid.Empty);
            
            // LC012: Using RemoveRange instead of ExecuteDelete
            // This loads entities into memory first
            db.Users.RemoveRange(usersToDelete);
            
            // Or on DbContext directly
            db.RemoveRange(usersToDelete);
        }
    }
}
