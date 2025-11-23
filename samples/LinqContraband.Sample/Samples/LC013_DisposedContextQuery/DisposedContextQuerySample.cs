using System.Collections.Generic;
using System.Linq;
using LinqContraband.Sample.Data;

namespace LinqContraband.Sample.Samples.LC013_DisposedContextQuery
{
    public class DisposedContextQuerySample
    {
        // Should trigger LC013
        public IQueryable<User> GetUsers_Violation()
        {
            using var db = new AppDbContext();
            // Violation: Returning a query from a context that is about to be disposed.
            return db.Users.Where(u => u.Age > 18);
        }

        // Should NOT trigger LC013 (Materialized)
        public List<User> GetUsers_Valid()
        {
            using var db = new AppDbContext();
            // Valid: Materialized before return
            return db.Users.Where(u => u.Age > 18).ToList();
        }
        
        // Should NOT trigger LC013 (External Context)
        public IQueryable<User> GetUsers_External(AppDbContext db)
        {
            // Valid: Context lifetime managed by caller
            return db.Users.Where(u => u.Age > 18);
        }
    }
}
