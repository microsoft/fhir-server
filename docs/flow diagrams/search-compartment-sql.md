```mermaid
%%{init: {'theme': 'neutral' } }%%
flowchart LR
SearchOptionsFactory -- Expression --> SqlServerSearchService

subgraph Fhir.Core

    SearchOptionsFactory
end

subgraph Fhir.SqlServer

    SqlServerSearchService -- Expression --> Rewriters
    
    Rewriters -- SqlRootExpression --> SqlQueryGenerator

    SqlQueryGenerator

    subgraph Rewriters

        CompartmentSearchRewriter -- UnionAllExpression --> SqlRootExpressionRewriter
        SqlRootExpressionRewriter -- SqlRootExpression --> PartitionEliminationRewriter
        PartitionEliminationRewriter -- SqlRootExpression --> TopRewriter
    end
end
```