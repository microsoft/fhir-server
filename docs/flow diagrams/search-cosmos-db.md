```mermaid
sequenceDiagram
    FhirCosmosSearchService->>SearchInternalAsync: SearchOptions
    SearchInternalAsync->>QueryBuilder: BuildSqlQuerySpec
    QueryBuilder->>QueryBuilderHelper: BuildSqlQuerySpec
    QueryBuilderHelper->>Expression: Accept Visitor
    Expression-->>QueryBuilderHelper: SqlQuerySpec
    QueryBuilderHelper-->>QueryBuilder: QueryDefinition
    QueryBuilder-->>SearchInternalAsync: SearchOptions
    SearchInternalAsync->>ExecuteSearchAsync: QueryDefinition
    ExecuteSearchAsync->>CosmosFhirDataStore: Execute Query on SQLQuerySpec
    CosmosFhirDataStore-->>ExecuteSearchAsync: FeedResponse
    ExecuteSearchAsync-->>SearchInternalAsync: SearchResult
    SearchInternalAsync-->>FhirCosmosSearchService: SearchResult
```