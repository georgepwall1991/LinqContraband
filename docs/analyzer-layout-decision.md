# Analyzer Layout Decision

## Decision

Adopt domain-based physical grouping for analyzer source folders while keeping the existing rule ids, namespaces, diagnostics, test layout, sample layout, and documentation layout unchanged.

## Why

The repository already has a logical taxonomy in `RuleCatalog`, `README.md`, and `docs/rule-catalog.md`, but the source tree still presents analyzers as one flat numeric corridor. Grouping the source folders by domain makes the implementation easier to navigate without changing the public analyzer surface.

## Trade-offs considered

### Keep the flat layout
Pros:
- zero migration cost
- existing folder expectations remain unchanged
- easy to add one more rule numerically

Cons:
- source layout hides rule families
- navigation cost grows with every new rule
- physical structure stays misaligned with the catalog taxonomy

### Group by domain
Pros:
- source tree reflects the existing rule neighborhoods
- easier to find related analyzers and shared behaviors
- reduces the “single corridor” effect in the main source layout

Cons:
- requires updating architecture tests and contributor docs
- introduces a migration cost for any tooling that assumes a flat source path
- needs a clear contract so future rules land in the correct neighborhood

## Constraints honored

- analyzer ids remain unchanged
- namespaces remain unchanged
- tests, docs, and samples keep their current flat `LCxxx_Name` layout
- repository governance remains enforceable through `RuleCatalog`

## Migration strategy

1. Add analyzer source paths to `RuleCatalog`.
2. Move analyzer source folders under domain directories.
3. Update architecture tests to validate source layout via catalog metadata instead of a flat-path assumption.
4. Update contributor docs to show the grouped source layout while preserving the existing test/sample/docs contract.

## Domain folders

- `BulkOperationsAndSetBasedWrites`
- `ChangeTrackingAndContextLifetime`
- `ExecutionAndAsync`
- `LoadingAndIncludes`
- `MaterializationAndProjection`
- `QueryShapeAndTranslation`
- `RawSqlAndSecurity`
- `SchemaAndModeling`

## Long-term rule

The catalog is the source of truth for physical source placement. New analyzer source folders should be created under the domain folder implied by the `RuleCatalog` entry, not directly under `src/LinqContraband/Analyzers/`.
