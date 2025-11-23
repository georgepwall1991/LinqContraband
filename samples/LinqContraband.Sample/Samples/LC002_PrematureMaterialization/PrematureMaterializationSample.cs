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

            // Same issue with AsEnumerable(): forces client-side evaluation before filtering.
            var prematureAsEnumerable = users.AsEnumerable().Where(u => u.Age > 30).ToList();

            // And with other materializers like ToDictionary.
            var prematureDictionary = users.ToDictionary(u => u.Id).Where(kvp => kvp.Value.Age > 25).ToList();
        }
    }
}
