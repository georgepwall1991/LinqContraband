using System.Diagnostics.CodeAnalysis;
using LinqContraband.Sample.Data;
using System;

namespace LinqContraband.Sample.Samples.LC014_AvoidStringCaseConversion;

/// <summary>
///     Demonstrates the "String Case Conversion" violation (LC014).
/// </summary>
/// <remarks>
///     <para>
///         <strong>The Crime:</strong> Using <c>.ToLower()</c> or <c>.ToUpper()</c> on a database column inside a LINQ
///         query predicate.
///     </para>
///     <para>
///         <strong>Why it's bad:</strong> This is "Non-Sargable" (Search ARGument ABLE). It forces the database to
///         transform
///         <em>every single row's value</em> to lowercase before comparing it. Because the value is transformed,
///         the database cannot use its B-Tree index on the column. This turns a fast <c>Index Seek</c> (O(log n))
///         into a slow <c>Index Scan</c> or <c>Table Scan</c> (O(n)).
///     </para>
///     <para>
///         <strong>The Fix:</strong> Use case-insensitive collation, a normalized search column, or
///         provider-specific collation support such as <c>EF.Functions.Collate</c> when it remains index-friendly.
///     </para>
/// </remarks>
[SuppressMessage("Performance", "LC009:Performance: Missing AsNoTracking() in Read-Only path")]
public static class AvoidStringCaseConversionSample
{
    public static void Run(AppDbContext db)
    {
        Console.WriteLine("Testing LC014...");

        // ====================================================================================
        // THE CRIME: Blocking Index Usage
        // ====================================================================================

        // This query forces the database to scan the entire Users table.
        // SQL Translation: SELECT ... WHERE LOWER(u.Name) = 'admin'
        var slowQuery = db.Users
            .Where(u => u.Name.ToLower() == "admin") // LC014 Violation
            .ToList();

        // Even in OrderBy, this prevents using the index for sorting, forcing a sort in memory/tempdb.
        var slowSort = db.Users
            .OrderBy(u => u.Name.ToUpper()) // LC014 Violation
            .ToList();

        // ====================================================================================
        // THE FIX: Efficient Index Usage
        // ====================================================================================

        // Option 1: Rely on Database Collation (Best Practice)
        // If your column is defined as Case-Insensitive (CI) in the database (e.g., SQL_Latin1_General_CP1_CI_AS),
        // simple equality is already case-insensitive and uses the index.
        var fastQuery1 = db.Users
            .Where(u => u.Name == "admin")
            .ToList();

        // Option 2: Pre-normalize the search value, not the column; pair this with matching schema/collation design.
        var fastQuery2 = db.Users
            .Where(u => u.Name == "ADMIN")
            .ToList();

        // Option 3: Explicit Collation (Postgres/Others)
        // Forces a specific collation for this comparison without breaking index usage (if supported by index).
        // var fastQuery3 = db.Users
        //     .Where(u => EF.Functions.Collate(u.Name, "SQL_Latin1_General_CP1_CI_AS") == "admin")
        //     .ToList();
    }
}
