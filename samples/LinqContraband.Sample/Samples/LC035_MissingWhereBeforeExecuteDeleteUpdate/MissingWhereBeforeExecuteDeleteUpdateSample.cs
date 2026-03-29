using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public static class MissingWhereBeforeExecuteDeleteUpdateSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC035...");

        // ADVISORY: Unfiltered bulk delete/update touches the whole table.
        db.Users.ExecuteDelete();
        db.Users.ExecuteUpdate(setters => setters.SetProperty(user => user.Name, "Archived"));

        // CORRECT: Filter the target set before executing the bulk command.
        db.Users.Where(user => user.Age < 18).ExecuteDelete();
    }
}
