```mermaid
%%{init: {'theme': 'neutral' } }%%
flowchart LR
SearchOptionsFactory -- Expression --> SqlServerSearchService

subgraph Core
    SearchOptionsFactory
end

subgraph SQL
    SqlServerSearchService -- UnionAllExpression --> Rewriters
    
    Rewriters -- SqlRootExpression --> SqlQueryGenerator

    SqlQueryGenerator --> SQL_Generation_Logic

    subgraph Rewriters
        CompartmentSearchRewriter -- Expression --> SqlRootExpressionRewriter
        SqlRootExpressionRewriter -- SqlRootExpression --> PartitionEliminationRewriter
        PartitionEliminationRewriter -- SqlRootExpression --> TopRewriter
    end
end
```