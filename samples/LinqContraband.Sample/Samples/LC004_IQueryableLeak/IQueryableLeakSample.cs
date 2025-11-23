using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC004_IQueryableLeak
{
    public class IQueryableLeakSample
    {
        public static void Run()
        {
            Console.WriteLine("Testing LC004...");
            using var db = new AppDbContext();
            
            // LC004: Passing IQueryable to method taking IEnumerable
            ProcessUsers(db.Users);
        }

        private static void ProcessUsers(IEnumerable<User> users)
        {
            // Iterating triggers execution
            foreach (var user in users)
            {
                Console.WriteLine(user.Id);
            }
        }
    }
}
