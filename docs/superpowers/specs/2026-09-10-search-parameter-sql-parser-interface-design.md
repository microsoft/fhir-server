# Search Parameter SQL Parser Interface Design

## Context

`SqlServerSearchService` currently depends directly on `SearchParameterSqlParser`. Service unit tests attempt to substitute that concrete class with NSubstitute, but the parser has no parameterless constructor and cannot be proxied using `Substitute.For<SearchParameterSqlParser>()`. The tests therefore fail during fixture construction before exercising service behavior.

The parser also acts as an internal parser factory for chained and reverse-chained searches through `GetParser`. That implementation detail is not used by `SqlServerSearchService` and should not be added to its dependency contract.

## Decision

Introduce a public `ISearchParameterSqlParser` interface in the SQL search parser namespace. The interface will expose only the `ParseMultiple` operation consumed by `SqlServerSearchService`, including its existing parameters and optional continuation tokens.

`SearchParameterSqlParser` will implement `ISearchParameterSqlParser` without changing parsing behavior or method signatures. Internal parser composition will continue to use the concrete `SearchParameterSqlParser` type where `GetParser` is required.

Change the `SqlServerSearchService` field and constructor parameter from `SearchParameterSqlParser` to `ISearchParameterSqlParser`. Tests and shared integration fixtures that substitute the parser for service construction will substitute the interface instead. Parser-focused tests and tools that exercise the concrete implementation will continue constructing the concrete class.

## Dependency Injection

The existing registration uses:

```csharp
services.Add<SearchParameterSqlParser>()
    .Singleton()
    .AsSelf()
    .AsImplementedInterfaces();
```

Implementing `ISearchParameterSqlParser` therefore registers both the concrete parser and the interface without a registration change. Concrete resolution remains available to the chained and reverse-chained parser implementation, while `SqlServerSearchService` receives the interface.

## Alternatives Considered

### Make `ParseMultiple` virtual

This would allow a class proxy to override the method, but the proxy would still need to satisfy the concrete parser constructor. It would also leave the service coupled to an implementation when it only needs one operation.

### Expose `ParseMultiple` and `GetParser`

This would allow all parser consumers to use one interface, but it would expose parser-factory internals to service consumers. The broader contract is unnecessary for the current testing and dependency boundary.

### Add an adapter around `SearchParameterSqlParser`

An adapter would provide a substitutable service dependency but add another implementation and registration solely to forward one method. Having the parser implement the narrow interface directly is simpler.

## Behavior and Error Handling

The change is dependency inversion only. It must not alter:

- Generated SQL or SQL parameters.
- Search, paging, include, compartment, SMART, Member Match, chain, or reverse-chain behavior.
- Exception propagation, cancellation, or logging.
- Parser lifetime or concrete parser availability through dependency injection.
- Constructor validation behavior in `SqlServerSearchService`, except that tests now provide an interface substitute.

## Testing

Validation will cover:

- `SqlServerSearchServiceTests` fixture construction and constructor validation using `ISearchParameterSqlParser`.
- Shared integration fixture compilation with an interface substitute.
- Existing `SearchParameterSqlParser` tests to confirm the concrete implementation still satisfies parser behavior.
- SQL Server project build and focused SQL Server unit tests.
- Dependency-injection resolution through the existing implemented-interface registration.

No new parser behavior tests are required because the implementation and SQL generation are unchanged.

