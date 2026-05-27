# Disable Query Plan Reuse for SQL Server based on Search Parameter Statistics

## Context 
   Some users who have query plan reuse enabled have reported performance issues with SQL Server. This is because SQL Server may reuse a query plan that was generated based on skewed search parameter statistics, leading to inefficient query execution. Disabling query plan reuse when we detect skewed statistics can help ensure that SQL Server generates a new query plan based on the current parameter values, which can improve performance.
   Statistics can become skewed when there is a significant difference in the distribution of values for a search parameter, such as when one value is much more common than others. This can lead to SQL Server generating a query plan that is optimized for one value, which may not be efficient for other values. By disabling query plan reuse in these cases, we can help ensure that SQL Server generates a more efficient query plan for each execution, improving overall performance.

## Decision
   We will implement logic to detect skewed search parameter statistics and disable query plan reuse for SQL Server when such skew is detected. This will involve analyzing the distribution of values for search parameters and determining when the statistics are skewed enough to warrant disabling query plan reuse. The implementation will be designed to minimize any performance impact while ensuring that SQL Server generates efficient query plans based on the current parameter values.
   This will be done by having a background worker that periodically checks the statistics for search parameters and maintains a list of parameters with skewed statistics. When executing queries, we will check if any of the parameters used in the query have skewed statistics and disable query plan reuse for those queries if necessary.

## Status
   Accepted

## Consequences
   - Maintaining the list of skewed search parameters will add more load to the database, but the additional requests are low in number and complexity as they are referencing existing statistic tables.
   - Disabling query plan reuse will lead to more time spent on query compilation. However, this is expected to be offset by the improved performance of the generated query plans.
