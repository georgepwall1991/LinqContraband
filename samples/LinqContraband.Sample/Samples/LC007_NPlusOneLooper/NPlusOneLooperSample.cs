using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC007_NPlusOneLooper
{
    public class NPlusOneLooperSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC007...");
            var targetIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            // This executes a database query inside a loop.
            foreach (var id in targetIds)
            {
                var user = users.Where(u => u.Id == id).ToList();
            }
        }
    }
}

