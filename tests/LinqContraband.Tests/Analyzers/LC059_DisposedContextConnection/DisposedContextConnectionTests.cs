using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC059_DisposedContextConnection.DisposedContextConnectionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC059_DisposedContextConnection;

public class DisposedContextConnectionTests
{
    private const string Usings = @"
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
";

    internal const string EfMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static DbConnection GetDbConnection(this Infrastructure.DatabaseFacade databaseFacade) => null;
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public class DatabaseFacade { }
}

public class ShopContext : Microsoft.EntityFrameworkCore.DbContext
{
}

public static class ConnectionFactory
{
    public static DbConnection Create() => null;
}
";

    internal static string Wrap(string body) => Usings + @"
class Program
{
    async Task Run(ShopContext db, DbConnection other, CancellationToken ct)
    {
" + body + @"
    }
}
" + EfMock;

    [Theory]
    [InlineData(@"using var connection = {|#0:db.Database.GetDbConnection()|}; await connection.OpenAsync(ct);")]
    [InlineData(@"await using var connection = {|#0:db.Database.GetDbConnection()|}; await connection.OpenAsync(ct);")]
    [InlineData(@"using (var connection = {|#0:db.Database.GetDbConnection()|}) { await connection.OpenAsync(ct); }")]
    [InlineData(@"await using (var connection = {|#0:db.Database.GetDbConnection()|}) { await connection.OpenAsync(ct); }")]
    [InlineData(@"using ({|#0:db.Database.GetDbConnection()|}) { }")]
    [InlineData(@"using (var connection = {|#0:(DbConnection)db.Database.GetDbConnection()|}) { }")]
    [InlineData(@"using IDbConnection connection = {|#0:db.Database.GetDbConnection()|}; connection.Open();")]
    [InlineData(@"var connection = db.Database.GetDbConnection(); using ({|#0:connection|}) { connection.Open(); }")]
    [InlineData(@"var connection = db.Database.GetDbConnection(); connection.Open(); {|#0:connection.Dispose()|};")]
    [InlineData(@"var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct); await {|#0:connection.DisposeAsync()|};")]
    [InlineData(@"var connection = db.Database.GetDbConnection(); try { connection.Open(); } finally { {|#0:connection.Dispose()|}; }")]
    [InlineData(@"{|#0:db.Database.GetDbConnection().Dispose()|};")]
    public async Task DisposingTheContextConnection_Reports(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body), VerifyCS.Diagnostic().WithLocation(0));
    }

    [Theory]
    // Disposing a command created from the connection is fine.
    [InlineData(@"using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = ""SELECT 1"";")]
    [InlineData(@"var connection = db.Database.GetDbConnection(); using (var command = connection.CreateCommand()) { }")]
    // Closing is fine.
    [InlineData(@"var connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct); await connection.CloseAsync();")]
    [InlineData(@"var connection = db.Database.GetDbConnection(); connection.Open(); connection.Close();")]
    // Connections the code owns.
    [InlineData(@"using var connection = ConnectionFactory.Create(); connection.Open();")]
    [InlineData(@"using (other) { }")]
    [InlineData(@"other.Dispose();")]
    // The local may hold another connection.
    [InlineData(@"var connection = db.Database.GetDbConnection(); if (ct.CanBeCanceled) connection = ConnectionFactory.Create(); connection.Dispose();")]
    // Just using it.
    [InlineData(@"var connection = db.Database.GetDbConnection(); var state = connection.State;")]
    public async Task SafeShapes_DoNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(body));
    }
}
