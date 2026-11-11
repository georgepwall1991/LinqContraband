# N+1 benchmark

A ground-truth benchmark for the N+1 rules (LC007, LC010 and LC045). It answers one question with a number: of the code that really costs one database round trip per element, how much does LinqContraband report, and how often does it report code that does not?

The current numbers are in [results.md](results.md).

## How it works

1. **Run.** Every case in `Corpus/Cases/` is a static class with a `Run` method. The benchmark runs it against an in-memory SQLite database seeded with 3 customers, then again with 6, and counts the round trips with an EF Core interceptor.
2. **Prove.** A case is an N+1 only if the larger database cost it more round trips. A case is safe only if the count stayed the same. Each case carries a `[NPlusOne]` or `[Safe]` label, and the run fails if the measurement disagrees with the label, so no case is scored on its author's opinion.
3. **Analyze.** The same source files are compiled in-process and analyzed by every LinqContraband analyzer with the default configuration. A case is reported when LC007, LC010 or LC045 fires anywhere inside its class.
4. **Score.** Recall is the share of proven N+1 cases that were reported. The false-positive rate is the share of proven safe cases that were reported.

A round trip is one SQL command, except that one `SaveChanges` call counts as one however many rows it writes. Providers batch those rows; SQLite happens to send a command per row, which would otherwise make every bulk save look like a save per element.

## Running it

```bash
dotnet run --project tools/NPlusOneBenchmark/NPlusOneBenchmark.csproj              # print the report
dotnet run --project tools/NPlusOneBenchmark/NPlusOneBenchmark.csproj -- --check   # fail if results.md is stale (CI runs this)
dotnet run --project tools/NPlusOneBenchmark/NPlusOneBenchmark.csproj -- --write   # regenerate results.md
```

When a change to an analyzer alters what the benchmark reports, `--check` fails. Regenerate `results.md` and commit it with the change: the diff shows every case that was gained or lost, which is the review evidence for an N+1 change.

## The corpus

Each case is tagged with what it repeats and how the loop reaches the database:

- **Kind**: `Query` (a query, `Find` or explicit load), `LazyLoad` (a navigation read on a lazy-loading context) or `Save` (a save or bulk command).
- **Via**: `Direct`, `Lambda`, `Helper`, `QueryableHelper`, `Interface`, `Virtual`, `Delegate`, `Mediator`, `CrossMethod` or `CrossProject`. `CaseAttributes.cs` defines each one.

`Corpus/Infrastructure/` holds the model, the two contexts (`ShopDb`, and `LazyShopDb` with lazy-loading proxies) and `Layers.cs`: the repositories, services, unit of work and mediator a layered application puts between a loop and its query. Every method in the shared layers is a single round trip, so an N+1 report can only belong to a case. `tools/NPlusOneBenchmark.External` is a separate project the corpus sees as metadata only, the way an application sees an infrastructure project or a repository package.

The safe cases are the shapes a pattern matcher is most likely to get wrong: batching, paging, retry loops, memoized lookups, loops that exit on the first iteration, entities the change tracker already holds.

## Adding a case

1. Add a static class to the matching file in `Corpus/Cases/`, with a `Run(ShopDb db)` or `Run(LazyShopDb db)` method (it may return a `Task`) and a `[NPlusOne(kind, via)]` or `[Safe(kind, via)]` attribute. Give it the next free number in its family (`Q` query, `L` lazy load, `W` write, `S` safe).
2. Iterate over a table that grows with the data (customers, orders, order lines, products). Regions and categories are fixed lookup tables.
3. Keep the corpus on C# 13 with explicit usings: the analysis pass compiles it with Roslyn 4.14.
4. Run with `--write` and commit `results.md`.

Write cases from code you have seen in real applications. A case added only because the analyzer happens to catch it, or happens to miss it, makes the number meaningless.

## Reading the numbers

- The headline recall depends on the mix of cases, so read the breakdown by `Via` first. It shows which layers the analyzers can see through.
- LC045 reports a navigation read without a matching `Include`. That is what a lazy-load N+1 looks like, but it also reports reads that are not per element, and a few safe cases count against it for that reason.
- Three safe cases are reported on purpose. `S13` memoizes a lookup in a dictionary: one query per distinct key is safe here only because regions are a fixed table, which the analyzer cannot know. `S14` calls `Find` for entities an earlier `Include` already tracks, so no query runs, but nothing in the loop proves that. `S17` reads a navigation on a single entity without an `Include`, which is one extra query on a lazy-loading context and a null reference without one.
- The score is what the analyzer package reports in a build, one project at a time, so `CrossProject` cases stay missed here by construction. `LinqContraband.Scan` builds both projects and follows a per-item call into the other one: a scan of this project reports Q85, Q87, Q88 and W12 and stays quiet on S37. Q86, where the other project returns an `IQueryable` and the loop executes it, is the one it does not follow.
- The ground truth comes from EF Core 10 on SQLite. Nothing in the corpus depends on SQLite behaviour beyond the `SaveChanges` rule above.
- The corpus was written for this repository. It shares no code with any other tool's benchmark, so its numbers are not comparable with theirs.
