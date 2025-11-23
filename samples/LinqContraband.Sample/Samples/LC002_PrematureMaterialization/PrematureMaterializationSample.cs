using System;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC002_PrematureMaterialization
{
    public class PrematureMaterializationSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC002...");
            // This calls ToList() (materializing all records) before filtering with Where().
            var prematureResult = users.ToList().Where(u => u.Age > 20).ToList();
        }
    }
}

