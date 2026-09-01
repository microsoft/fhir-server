// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Handles basic system-level searches when no search parameters are provided.
    /// Used for queries like GET [base]/Patient or GET [base]?_type=Patient
    /// </summary>
    public class SystemSqlParser : ISqlParser
    {
        public void Parse(string name, string value, ParserOptions options)
        {
            // SystemSqlParser doesn't use name/value parameters
            // It generates a basic query based on options only
            var sqlBuilder = options.SqlQueryBuilder;

            sqlBuilder.BeginCte($"cte{options.CteNumber}");

            // Build the SELECT clause with TOP or without based on whether we're counting
            sqlBuilder.Select("r.ResourceTypeId", "r.ResourceSurrogateId");

            // FROM clause - always from dbo.Resource for system queries
            sqlBuilder.From("dbo.Resource", "r");

            // WHERE clause - base filters
            sqlBuilder.Where("r.IsHistory = 0")
                .And("r.IsDeleted = 0");

            // Add resource type filter if specified
            if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
            {
                var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                sqlBuilder.And($"r.ResourceTypeId IN ({resourceTypeIds})");
            }

            // Add continuation token support
            ParserUtil.AddFirstCteFilters(sqlBuilder, options, "r");

            sqlBuilder.EndCte();
        }
    }
}
