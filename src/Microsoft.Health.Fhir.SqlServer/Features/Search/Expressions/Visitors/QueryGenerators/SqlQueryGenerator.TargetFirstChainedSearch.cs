// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Generates the target-first query shape for selective forward chained searches.
    /// </summary>
    internal partial class SqlQueryGenerator
    {
        private int TryAppendTargetFirstChainedSearchCtes(
            IReadOnlyList<SearchParamTableExpression> tableExpressions,
            SearchOptions context)
        {
            if (!TryMatchTargetFirstChainedSearch(
                    tableExpressions,
                    context,
                    out SqlChainLinkExpression chainExpression,
                    out SearchParamTableExpression targetExpression,
                    out SearchParamTableExpression sourceDateExpression,
                    out SearchParamTableExpression sortExpression))
            {
                return 0;
            }

            AppendTargetFirstChainedSearchCte(() => AppendTargetFirstChainedSearchTarget(targetExpression, chainExpression, context));
            int targetCteId = _tableExpressionCounter;
            AppendTargetFirstChainedSearchCte(() => AppendTargetFirstChainedSearchSource(chainExpression, targetCteId));
            int sourceCteId = _tableExpressionCounter;
            AppendTargetFirstChainedSearchCte(() => AppendTargetFirstChainedSearchSourceDateFilter(sourceDateExpression, context, sourceCteId));

            if (sortExpression != null)
            {
                int filteredSourceCteId = _tableExpressionCounter;
                AppendTargetFirstChainedSearchCte(() => AppendTargetFirstChainedSearchSort(sortExpression, context, filteredSourceCteId));
                _sortVisited = true;
            }

            return sortExpression == null ? 3 : 4;
        }

        private bool TryMatchTargetFirstChainedSearch(
            IReadOnlyList<SearchParamTableExpression> tableExpressions,
            SearchOptions context,
            out SqlChainLinkExpression chainExpression,
            out SearchParamTableExpression targetExpression,
            out SearchParamTableExpression sourceDateExpression,
            out SearchParamTableExpression sortExpression)
        {
            chainExpression = null;
            targetExpression = null;
            sourceDateExpression = null;
            sortExpression = null;

            const int chainExpressionIndex = 0;
            const int targetExpressionIndex = 1;
            const int sourceDateExpressionIndex = 2;
            const int sortExpressionIndex = 3;
            if (_tableExpressionCounter != -1 ||
                tableExpressions.Count <= sourceDateExpressionIndex ||
                tableExpressions.Any(x => x.HasUnionAllExpression()))
            {
                return false;
            }

            SearchParamTableExpression chainCandidate = tableExpressions[chainExpressionIndex];
            if (chainCandidate.Kind != SearchParamTableExpressionKind.Chain ||
                chainCandidate.ChainLevel != 1 ||
                chainCandidate.Predicate is not SqlChainLinkExpression chainCandidateExpression ||
                chainCandidateExpression.Reversed ||
                chainCandidateExpression.ExpressionOnTarget != null)
            {
                return false;
            }

            SearchParamTableExpression targetCandidate = tableExpressions[targetExpressionIndex];
            SearchParamTableExpression sourceDateCandidate = tableExpressions[sourceDateExpressionIndex];
            if (targetCandidate.Kind != SearchParamTableExpressionKind.Normal ||
                targetCandidate.ChainLevel != chainCandidate.ChainLevel ||
                !ReferenceEquals(targetCandidate.QueryGenerator, ReferenceQueryGenerator.Instance) ||
                sourceDateCandidate.Kind != SearchParamTableExpressionKind.Normal ||
                sourceDateCandidate.ChainLevel != 0 ||
                !ReferenceEquals(sourceDateCandidate.QueryGenerator, DateTimeQueryGenerator.Instance))
            {
                return false;
            }

            if (tableExpressions.Count > sortExpressionIndex &&
                tableExpressions[sortExpressionIndex].Kind is SearchParamTableExpressionKind.Sort or SearchParamTableExpressionKind.SortWithFilter)
            {
                SearchParamTableExpression sortCandidate = tableExpressions[sortExpressionIndex];
                SortContext sortContext = GetSortRelatedDetails(context);
                if (sortCandidate.ChainLevel != 0 ||
                    sortCandidate.QueryGenerator == null ||
                    string.IsNullOrEmpty(sortContext.SortColumnName))
                {
                    return false;
                }

                sortExpression = sortCandidate;
            }

            chainExpression = chainCandidateExpression;
            targetExpression = targetCandidate;
            sourceDateExpression = sourceDateCandidate;
            return true;
        }

        private void AppendTargetFirstChainedSearchCte(Action appendBody)
        {
            if (_tableExpressionCounter >= 0)
            {
                StringBuilder.AppendLine().Append(",");
            }

            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");
            using (StringBuilder.Indent())
            {
                appendBody();
            }

            StringBuilder.Append(")");
        }

        private void AppendTargetFirstChainedSearchTarget(
            SearchParamTableExpression targetExpression,
            SqlChainLinkExpression chainExpression,
            SearchOptions context)
        {
            const string chainTargetTableAlias = "chainTarget";
            const string referenceTargetResourceTableAlias = "refTarget";

            StringBuilder.Append("SELECT ")
                .Append(VLatest.Resource.ResourceTypeId, chainTargetTableAlias).Append(" AS T2, ")
                .Append(VLatest.Resource.ResourceSurrogateId, chainTargetTableAlias).Append(" AS Sid2, ")
                .Append(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias).AppendLine(" AS Id2")
                .Append("FROM ").Append(targetExpression.QueryGenerator.Table).Append(' ').AppendLine(chainTargetTableAlias)
                .Append(_joinShift).Append("INNER LOOP JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceTargetResourceTableAlias)
                .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, chainTargetTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias)
                .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, chainTargetTableAlias).Append(" = ").AppendLine(VLatest.Resource.ResourceSurrogateId, referenceTargetResourceTableAlias);

            using var delimited = StringBuilder.BeginDelimitedWhereClause();
            AppendHistoryClause(delimited, context.ResourceVersionTypes, null, referenceTargetResourceTableAlias);
            AppendDeletedClause(delimited, context.ResourceVersionTypes, referenceTargetResourceTableAlias);

            delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceTypeId, chainTargetTableAlias)
                .Append(" IN (")
                .Append(string.Join(", ", chainExpression.TargetResourceTypes.Select(x => Parameters.AddParameter(VLatest.Resource.ResourceTypeId, Model.GetResourceTypeId(x), true))))
                .Append(")");

            delimited.BeginDelimitedElement();
            CheckForIdentifierSearchParams(targetExpression.Predicate);
            targetExpression.Predicate.AcceptVisitor(targetExpression.QueryGenerator, GetContext(chainTargetTableAlias));
        }

        private void AppendTargetFirstChainedSearchSource(SqlChainLinkExpression chainExpression, int targetCteId)
        {
            const string referenceSourceTableAlias = "refSource";

            StringBuilder.Append("SELECT ")
                .Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias).Append(" AS T1, ")
                .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).AppendLine(" AS Sid1, T2, Sid2")
                .Append("FROM ").AppendLine(TableExpressionName(targetCteId))
                .Append(_joinShift).Append("INNER LOOP JOIN ").Append(VLatest.ReferenceSearchParam).Append(' ').Append(referenceSourceTableAlias)
                .Append(" ON ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = T2")
                .Append(" AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias).AppendLine(" = Id2");

            using var delimited = StringBuilder.BeginDelimitedWhereClause();
            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.SearchParamId, referenceSourceTableAlias)
                .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(chainExpression.ReferenceSearchParameter.Url), true));

            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                .Append(" IN (")
                .Append(string.Join(", ", chainExpression.ResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(x), true))))
                .Append(")");

            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                .Append(" IN (")
                .Append(string.Join(", ", chainExpression.TargetResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x), true))))
                .Append(")");

            if (chainExpression.ExpressionOnSource != null)
            {
                delimited.BeginDelimitedElement();
                chainExpression.ExpressionOnSource.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(referenceSourceTableAlias));
            }
        }

        private void AppendTargetFirstChainedSearchSourceDateFilter(
            SearchParamTableExpression dateExpression,
            SearchOptions context,
            int sourceCteId)
        {
            StringBuilder.Append("SELECT ")
                .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                .Append("FROM ").AppendLine(dateExpression.QueryGenerator.Table)
                .Append(_joinShift).Append("INNER HASH JOIN ").Append(TableExpressionName(sourceCteId))
                .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = T1")
                .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" = Sid1");

            using var delimited = StringBuilder.BeginDelimitedWhereClause();
            AppendHistoryClause(delimited, context.ResourceVersionTypes, dateExpression, null, dateExpression.QueryGenerator.Table);

            delimited.BeginDelimitedElement();
            CheckForIdentifierSearchParams(dateExpression.Predicate);
            dateExpression.Predicate.AcceptVisitor(dateExpression.QueryGenerator, GetContext());
        }

        private void AppendTargetFirstChainedSearchSort(
            SearchParamTableExpression sortExpression,
            SearchOptions context,
            int filteredSourceCteId)
        {
            SortContext sortContext = GetSortRelatedDetails(context);
            string filteredSourceCte = TableExpressionName(filteredSourceCteId);
            StringBuilder.Append("SELECT ")
                .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                .Append(sortContext.SortColumnName, null).AppendLine(" AS SortValue")
                .Append("FROM ").AppendLine(sortExpression.QueryGenerator.Table)
                .Append(_joinShift).Append("JOIN ").Append(filteredSourceCte)
                .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(filteredSourceCte).Append(".T1")
                .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(filteredSourceCte).AppendLine(".Sid1");

            using var delimited = StringBuilder.BeginDelimitedWhereClause();
            AppendHistoryClause(delimited, context.ResourceVersionTypes, sortExpression);
            AppendMinOrMax(delimited, context);

            if (sortExpression.Predicate != null)
            {
                delimited.BeginDelimitedElement();
                sortExpression.Predicate.AcceptVisitor(sortExpression.QueryGenerator, GetContext());
            }

            AppendSortContinuationPredicate(delimited, sortContext);
        }
    }
}
