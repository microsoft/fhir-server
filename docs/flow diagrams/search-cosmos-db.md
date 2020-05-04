```mermaid
sequenceDiagram
    FhirCosmosSearchService->>SearchInternalAsync: SearchOptions
    SearchInternalAsync->>QueryBuilder: BuildSqlQuerySpec
    QueryBuilder->>QueryBuilderHelper: BuildSqlQuerySpec
    QueryBuilderHelper->>Expression: Accept Visitor
    Expression->>QueryBuilderHelper: SqlQuerySpec
    QueryBuilderHelper->>QueryBuilder:
    QueryBuilder->>SearchInternalAsync:
    SearchInternalAsync->>ExecuteSearchAsync:
    ExecuteSearchAsync->>CosmosFhirDataStore: Execute Query on SQLQuerySpec
    CosmosFhirDataStore->>ExecuteSearchAsync: FeedResponse
    ExecuteSearchAsync->>SearchInternalAsync: SearchResult
    SearchInternalAsync->>FhirCosmosSearchService: SearchResult
```