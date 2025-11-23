using System;
using System.Collections.Generic;
using System.Linq;
using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC009_MissingAsNoTracking
{
    public class MissingAsNoTrackingSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC009...");
            // This method returns entities but doesn't use AsNoTracking() and doesn't save changes.
            GetUsersReadOnly(users);

            // A read-only path that explicitly opts into identity resolution tracking optimization.
            GetUsersWithIdentityResolution(users);
        }

        static List<User> GetUsersReadOnly(IQueryable<User> users)
        {
            // Violation: Returning entities from read-only context without AsNoTracking
            return users.Where(u => u.Age > 18).ToList();
        }

        static List<User> GetUsersWithIdentityResolution(IQueryable<User> users)
        {
            // Safe: Using AsNoTrackingWithIdentityResolution to avoid tracking overhead
            return users.AsNoTrackingWithIdentityResolution().Where(u => u.Age > 21).ToList();
        }
    }
}
