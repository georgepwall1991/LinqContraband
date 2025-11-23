using System;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC005_MultipleOrderBy
{
    public class MultipleOrderBySample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC005...");
            // This calls OrderBy twice, resetting the first sort instead of chaining with ThenBy.
            var orderResult = users.OrderBy(u => u.Age).OrderBy(u => u.Name).ToList();
        }
    }
}

