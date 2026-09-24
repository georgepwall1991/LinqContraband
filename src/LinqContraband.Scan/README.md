# LinqContraband.Scan

Scan any .NET solution for EF Core LINQ problems without adding a package to it.

```bash
dnx LinqContraband.Scan                 # .NET 10 SDK, no install step
dnx LinqContraband.Scan -- MyApp.sln -o results.sarif

dotnet tool install -g LinqContraband.Scan   # .NET 8 or 9 SDK
linqcontraband-scan MyApp.sln
```

The scanner builds your solution with the [LinqContraband](https://www.nuget.org/packages/LinqContraband) analyzers injected, then prints the rules that fired, how often, where (with the line of code), the most affected files and a link to each rule's page. It also writes a SARIF 2.1.0 file you can upload to GitHub code scanning. Your project files are not changed.

Output looks like this:

```text
LinqContraband found 14 problems (5 rules, 6 files).

  Rule   Severity Count  Title
  LC007  Warning      6  N+1 Problem: Database execution inside loop
  LC009  Info         4  Performance: Missing AsNoTracking() in Read-Only path
  ...

Findings:

  LC007  N+1 Problem: Database execution inside loop
    src/Orders/OrderService.cs:52:31
      Executing 'FirstOrDefaultAsync' inside a loop causes N+1 database operations. Fetch data in bulk or eager load before the loop.
      52 | var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == order.CustomerId);
  ...
```

`--html report.html` writes the whole report as one page you can share, with the code around every finding. In CI, `--fail-on warning` fails the build on findings, `--baseline <earlier.sarif>` fails only on new ones, and `--exclude 'tests/**'` leaves files out. In GitHub Actions the scan writes a job summary and annotates the pull request, and the repository is also an Action: `uses: georgepwall1991/LinqContraband@v5.12.0`.

To keep the checks on in the editor and in CI, add the analyzer package: `dotnet add package LinqContraband`.

Full guide: https://georgepwall1991.github.io/LinqContraband/ef-core-query-scanner/
