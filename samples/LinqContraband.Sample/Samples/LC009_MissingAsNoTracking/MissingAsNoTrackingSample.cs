using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC009_MissingAsNoTracking
{
    public class MissingAsNoTrackingSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC009...");
            // This method returns entities but doesn't use AsNoTracking() and doesn't save changes.
            GetUsersReadOnly(users);
        }

        static List<User> GetUsersReadOnly(IQueryable<User> users)
        {
            // Violation: Returning entities from read-only context without AsNoTracking
            return users.Where(u => u.Age > 18).ToList();
        }
    }
}

