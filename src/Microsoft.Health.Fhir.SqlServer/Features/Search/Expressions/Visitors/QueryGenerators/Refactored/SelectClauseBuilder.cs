// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored
{
    /// <summary>
    /// Helper class for building SELECT clauses.
    /// </summary>
    internal class SelectClauseBuilder
    {
        public static void BuildSelectClause(QueryGenerationContext context)
        {
            if (context.SearchOptions.CountOnly)
            {
                BuildCountSelect(context);
            }
            else
            {
                BuildResourceSelect(context);
            }
        }

        private static void BuildCountSelect(QueryGenerationContext context)
        {
            var sb = context.StringBuilder;

            if (context.RootExpression.SearchParamTableExpressions.Count > 0)
            {
                sb.AppendLine("SELECT count_big(DISTINCT Sid1)");
                sb.Append("FROM ").AppendLine(context.GetCurrentCteTableName());
            }
            else
            {
                sb.AppendLine("SELECT count_big(*)");
                BuildResourceTableJoin(context);
            }
        }

        private static void BuildResourceSelect(QueryGenerationContext context)
        {
            var sb = context.StringBuilder;

            // Outer query with TOP for pagination
            if (context.RootExpression.SearchParamTableExpressions.Count == 0)
            {
                sb.Append("SELECT TOP (")
                    .Append(context.Parameters.AddParameter(context.SearchOptions.MaxItemCount + 1, includeInHash: false))
                    .Append(") * FROM (");
            }
            else
            {
                sb.Append("SELECT * FROM (");
            }

            BuildResourceColumns(context);
            BuildResourceTableJoin(context);
            BuildOrderByClause(context);
        }

        private static void BuildResourceColumns(QueryGenerationContext context)
        {
            var sb = context.StringBuilder;
            const string alias = "r";

            sb.Append("SELECT DISTINCT ");
            sb.Append(VLatest.Resource.ResourceTypeId, alias).Append(", ")
                .Append(VLatest.Resource.ResourceId, alias).Append(", ")
                .Append(VLatest.Resource.Version, alias).Append(", ")
                .Append(VLatest.Resource.IsDeleted, alias).Append(", ")
                .Append(VLatest.Resource.ResourceSurrogateId, alias).Append(", ")
                .Append(VLatest.Resource.RequestMethod, alias).Append(", ");

            bool hasTableExpressions = context.RootExpression.SearchParamTableExpressions.Count > 0;
            sb.Append(hasTableExpressions ? "CAST(IsMatch AS bit) AS IsMatch, " : "CAST(1 AS bit) AS IsMatch, ");
            sb.Append(hasTableExpressions ? "CAST(IsPartial AS bit) AS IsPartial, " : "CAST(0 AS bit) AS IsPartial, ");

            sb.Append(VLatest.Resource.IsRawResourceMetaSet, alias).Append(", ");

            if (context.SchemaInfo.Current >= SchemaVersionConstants.SearchParameterHashSchemaVersion)
            {
                sb.Append(VLatest.Resource.SearchParamHash, alias).Append(", ");
            }

            sb.Append(VLatest.Resource.RawResource, alias);

            if (Helpers.SortingHelper.IsSortValueNeeded(context.SearchOptions) && !context.SearchOptions.IsIncludesOperation)
            {
                sb.Append(", ").Append(context.GetCurrentCteTableName()).Append(".SortValue");
            }

            sb.AppendLine();
        }

        private static void BuildResourceTableJoin(QueryGenerationContext context)
        {
            var sb = context.StringBuilder;
            const string alias = "r";

            sb.Append("FROM ").Append(VLatest.Resource).Append(" ").AppendLine(alias);

            if (context.RootExpression.SearchParamTableExpressions.Count > 0)
            {
                sb.Append("     JOIN ").Append(context.GetCurrentCteTableName())
                    .Append(" ON ")
                    .Append(VLatest.Resource.ResourceTypeId, alias).Append(" = ").Append(context.GetCurrentCteTableName()).Append(".T1 AND ")
                    .Append(VLatest.Resource.ResourceSurrogateId, alias).Append(" = ").Append(context.GetCurrentCteTableName()).AppendLine(".Sid1");
            }

            AppendResourceTableFilters(context, alias);
        }

        private static void AppendResourceTableFilters(QueryGenerationContext context, string alias)
        {
            var sb = context.StringBuilder;
            using (var delimited = sb.BeginDelimitedWhereClause())
            {
                foreach (var predicate in context.RootExpression.ResourceTableExpressions)
                {
                    delimited.BeginDelimitedElement();
                    var generatorContext = new SearchParameterQueryGeneratorContext(
                        context.StringBuilder,
                        context.Parameters,
                        context.Model,
                        context.SchemaInfo,
                        context.IsAsyncOperation,
                        alias);
                    predicate.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, generatorContext);
                }

                var historyBuilder = new ClauseBuilders.HistoryClauseBuilder();
                historyBuilder.AppendHistoryClause(delimited, context.SearchOptions.ResourceVersionTypes, context);

                var deletedBuilder = new ClauseBuilders.DeletedClauseBuilder();
                deletedBuilder.AppendDeletedClause(delimited, context.SearchOptions.ResourceVersionTypes, context);
            }
        }

        private static void BuildOrderByClause(QueryGenerationContext context)
        {
            if (context.SearchOptions.CountOnly)
            {
                return;
            }

            var sb = context.StringBuilder;
            const string orderTableAlias = "t";

            sb.Append(") AS ").Append(orderTableAlias).Append(" ORDER BY ");

            Helpers.SortingHelper.AppendOrderByClause(context, orderTableAlias);

            sb.AppendLine();
        }
    }
}
