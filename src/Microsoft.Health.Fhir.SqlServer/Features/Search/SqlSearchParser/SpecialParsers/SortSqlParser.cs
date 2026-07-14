// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Parses sort parameters to create SQL that joins with DateTimeSearchParam or StringSearchParam
    /// tables and uses IsMin/IsMax columns for efficient sorting.
    /// </summary>
    public class SortSqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;

        public SortSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            _parameterCollection = parameterCollection;
        }

        /// <summary>
        /// Creates a CTE that joins the main result set with the appropriate search parameter table
        /// to enable sorting by that parameter's values.
        /// </summary>
        /// <param name="sortParameterName">The name of the parameter to sort by.</param>
        /// <param name="sortDescending">True for descending sort, false for ascending.</param>
        /// <param name="sourceCteName">The name of the CTE containing the resources to sort.</param>
        /// <param name="targetCteName">The name to give the resulting sorted CTE.</param>
        /// <param name="resourceTypeId">The resource type ID to filter on, or 0 for all types.</param>
        /// <param name="continuationPoint">The continuation point to use for paging, or null for no continuation.</param>
        /// <returns>SQL string for the sort CTE, or null if the parameter is not sortable.</returns>
        public string? CreateSortCte(
            string sortParameterName,
            bool sortDescending,
            string sourceCteName,
            string targetCteName,
            short resourceTypeId,
            string? continuationPoint = null)
        {
            if (string.IsNullOrWhiteSpace(sortParameterName) || string.IsNullOrWhiteSpace(sourceCteName))
            {
                return null;
            }

            // Get the search parameter definition
            var parameter = _parameterCollection.GetByCode(sortParameterName, resourceTypeId);
            if (parameter == null)
            {
                return null;
            }

            // Only DateTime and String parameters support sorting with IsMin/IsMax
            if (parameter.SearchParameterInfo.Type != SearchParamType.Date &&
                parameter.SearchParameterInfo.Type != SearchParamType.String)
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"{targetCteName} AS (");
            sqlBuilder.AppendLine("  SELECT");
            sqlBuilder.AppendLine("    r.ResourceTypeId,");
            sqlBuilder.AppendLine("    r.ResourceSurrogateId,");
            sqlBuilder.AppendLine("    r.IsMatch,");
            sqlBuilder.AppendLine("    r.IsPartial,");
            sqlBuilder.AppendLine("    r.Row");

            // Determine which table and column to use
            string tableName;
            string sortColumn;
            string isMinMaxColumn = sortDescending ? "IsMax" : "IsMin";

            if (parameter.SearchParameterInfo.Type == SearchParamType.Date)
            {
                tableName = "dbo.DateTimeSearchParam";

                // For DateTime, we sort by StartDateTime (the beginning of the range)
                sortColumn = "sp.StartDateTime";
            }
            else // String
            {
                tableName = "dbo.StringSearchParam";

                // For String, we use the Text column
                sortColumn = "sp.Text";
            }

            sqlBuilder.AppendLine($"    ,{sortColumn} AS SortValue");
            sqlBuilder.AppendLine($"  FROM {sourceCteName} r");

            // Inner join to only include resources that have the search parameter
            sqlBuilder.AppendLine($"    JOIN {tableName} sp ON");
            sqlBuilder.AppendLine("      sp.ResourceTypeId = r.ResourceTypeId");
            sqlBuilder.AppendLine("      AND sp.ResourceSurrogateId = r.ResourceSurrogateId");
            sqlBuilder.AppendLine($"      AND sp.SearchParamId = {parameter.Id}");
            sqlBuilder.AppendLine($"      AND sp.{isMinMaxColumn} = 1");

            if (!string.IsNullOrEmpty(continuationPoint))
            {
                sqlBuilder.AppendLine($"      AND {sortColumn} {(sortDescending ? "<" : ">")}= '{continuationPoint}'");
            }

            sqlBuilder.Append(')');

            return sqlBuilder.ToString();
        }

        /// <summary>
        /// Creates the ORDER BY clause for a sorted query.
        /// </summary>
        /// <param name="sortDescending">True for descending sort, false for ascending.</param>
        /// <param name="hasSortValue">True if the query joined with a sort parameter table.</param>
        /// <returns>The ORDER BY clause SQL string.</returns>
        public static string CreateOrderByClause(bool sortDescending, bool hasSortValue)
        {
            if (!hasSortValue)
            {
                // No sort parameter - use default ordering
                return "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
            }

            var sqlBuilder = new StringBuilder("ORDER BY t.IsMatch DESC");

            if (sortDescending)
            {
                // Descending: NULLs last, then sort values descending
                // NULLS are resources without the parameter
                sqlBuilder.Append(", CASE WHEN t.SortValue IS NULL THEN 1 ELSE 0 END ASC, t.SortValue DESC");
            }
            else
            {
                // Ascending: NULLs last, then sort values ascending
                sqlBuilder.Append(", CASE WHEN t.SortValue IS NULL THEN 1 ELSE 0 END ASC, t.SortValue ASC");
            }

            // Add ResourceTypeId and ResourceSurrogateId as tie-breakers for stable sorting
            sqlBuilder.Append(", t.ResourceTypeId ASC, t.ResourceSurrogateId ASC");

            return sqlBuilder.ToString();
        }
    }
}
