using System;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC001_LocalMethod
{
    public class LocalMethodSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC001...");
            // This calls a local method inside an IQueryable expression, preventing SQL translation.
            var localResult = users.Where(u => IsAdult(u.Age)).ToList();
        }

        static bool IsAdult(int age) => age >= 18;
    }
}

