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
///         <strong>The Fix:</strong> Use case-insensitive collation, <c>string.Equals</c> with <c>OrdinalIgnoreCase</c>,
///         or <c>EF.Functions.Collate</c>.
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
            .Where(u => string.Equals(u.Name, "admin", StringComparison.OrdinalIgnoreCase)) // LC014 Violation
            .ToList();

        // Even in OrderBy, this prevents using the index for sorting, forcing a sort in memory/tempdb.
        var slowSort = db.Users
            .OrderBy(u => u.Name.ToUpper()) // LC014 Violation
            .ToList();

        // ====================================================================================
        // THE FIX: Efficient Index Usage
        // ====================================================================================

        // Option 1: Use string.Equals (if your DB provider translates it to case-insensitive SQL)
        // SQL Translation (e.g. SQL Server): SELECT ... WHERE u.Name = 'admin'
        var fastQuery1 = db.Users
            .Where(u => string.Equals(u.Name, "admin", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Option 2: Rely on Database Collation (Best Practice)
        // If your column is defined as Case-Insensitive (CI) in the database (e.g., SQL_Latin1_General_CP1_CI_AS),
        // simple equality is already case-insensitive and uses the index.
        var fastQuery2 = db.Users
            .Where(u => u.Name == "admin")
            .ToList();

        // Option 3: Explicit Collation (Postgres/Others)
        // Forces a specific collation for this comparison without breaking index usage (if supported by index).
        // var fastQuery3 = db.Users
        //     .Where(u => EF.Functions.Collate(u.Name, "SQL_Latin1_General_CP1_CI_AS") == "admin")
        //     .ToList();
    }
}
