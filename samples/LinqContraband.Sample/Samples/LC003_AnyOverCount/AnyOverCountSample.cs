using System;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC003_AnyOverCount
{
    public class AnyOverCountSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC003...");
            // This uses Count() > 0 to check existence, which may iterate the whole table.
            if (users.Count() > 0)
            {
                Console.WriteLine("Users exist");
            }
        }
    }
}

