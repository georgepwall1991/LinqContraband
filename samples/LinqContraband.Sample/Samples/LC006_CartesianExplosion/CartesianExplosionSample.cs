using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC006_CartesianExplosion
{
    public class CartesianExplosionSample
    {
        public static void Run(IQueryable<User> users)
        {
            Console.WriteLine("Testing LC006...");
            // This includes multiple collections in a single query without splitting.
            var cartesianResult = users.Include(u => u.Orders).Include(u => u.Roles).ToList();
        }
    }
}

