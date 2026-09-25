using LinqContraband.Sample.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqContraband.Sample.Samples.LC059_DisposedContextConnection;

// Not called from Program: the sample context uses the in-memory provider, which has no DbConnection.
public static class DisposedContextConnectionSample
{
    public static async Task<object?> CountUsersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // VIOLATION: the DbContext owns this connection. Disposing it here breaks every later
        // query and SaveChanges on the context.
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users";
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public static async Task<object?> CountUsersAndCloseAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // CORRECT: dispose the command, close the connection this code opened, and let the
        // context dispose the connection itself.
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Users";
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
