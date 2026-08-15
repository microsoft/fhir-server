// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
        private TargetFirstChainedSearchState _targetFirstChainedSearchState;

        private bool TryHandleTargetFirstChainedSearchChainExpression(
            SearchParamTableExpression tableExpression,
            SqlChainLinkExpression chainExpression,
            SearchOptions context)
        {
            if (!TryMatchTargetFirstChainedSearch(tableExpression, chainExpression, out SearchParamTableExpression targetExpression, out SearchParamTableExpression sourceDateExpression))
            {
                return false;
            }

            _targetFirstChainedSearchState = new TargetFirstChainedSearchState(chainExpression, targetExpression, sourceDateExpression);
            AppendTargetFirstChainedSearchTarget(targetExpression, chainExpression, context);
            return true;
        }

        private bool TryHandleTargetFirstChainedSearchNormalExpression(SearchParamTableExpression tableExpression, SearchOptions context)
        {
            TargetFirstChainedSearchState state = _targetFirstChainedSearchState;
            if (state == null)
            {
                return false;
            }

            if (ReferenceEquals(tableExpression, state.TargetExpression))
            {
                AppendTargetFirstChainedSearchSource(state.ChainExpression);
                state.SourceCteId = _tableExpressionCounter;
                return true;
            }

            if (ReferenceEquals(tableExpression, state.SourceDateExpression) &&
                state.SourceCteId == _tableExpressionCounter - 1)
            {
                AppendTargetFirstChainedSearchSourceDateFilter(tableExpression, context, state.SourceCteId);
                _targetFirstChainedSearchState = null;
                return true;
            }

            _targetFirstChainedSearchState = null;
            return false;
        }

        private bool TryMatchTargetFirstChainedSearch(
            SearchParamTableExpression tableExpression,
            SqlChainLinkExpression chainExpression,
            out SearchParamTableExpression targetExpression,
            out SearchParamTableExpression sourceDateExpression)
        {
            targetExpression = null;
            sourceDateExpression = null;

            if (_tableExpressionCounter != 0 ||
                tableExpression.ChainLevel != 1 ||
                chainExpression.Reversed ||
                chainExpression.ExpressionOnTarget != null ||
                _rootExpression.SearchParamTableExpressions.Any(x =>
                    x.HasUnionAllExpression() ||
                    x.Kind == SearchParamTableExpressionKind.Sort ||
                    x.Kind == SearchParamTableExpressionKind.SortWithFilter))
            {
                return false;
            }

            const int targetExpressionIndex = 1;
            const int sourceDateExpressionIndex = 2;
            if (_rootExpression.SearchParamTableExpressions.Count <= sourceDateExpressionIndex)
            {
                return false;
            }

            SearchParamTableExpression targetCandidate = _rootExpression.SearchParamTableExpressions[targetExpressionIndex];
            SearchParamTableExpression sourceDateCandidate = _rootExpression.SearchParamTableExpressions[sourceDateExpressionIndex];
            if (targetCandidate.Kind != SearchParamTableExpressionKind.Normal ||
                targetCandidate.ChainLevel != tableExpression.ChainLevel ||
                !ReferenceEquals(targetCandidate.QueryGenerator, ReferenceQueryGenerator.Instance) ||
                sourceDateCandidate.Kind != SearchParamTableExpressionKind.Normal ||
                sourceDateCandidate.ChainLevel != 0 ||
                !ReferenceEquals(sourceDateCandidate.QueryGenerator, DateTimeQueryGenerator.Instance))
            {
                return false;
            }

            targetExpression = targetCandidate;
            sourceDateExpression = sourceDateCandidate;
            return true;
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

        private void AppendTargetFirstChainedSearchSource(SqlChainLinkExpression chainExpression)
        {
            const string referenceSourceTableAlias = "refSource";

            StringBuilder.Append("SELECT ")
                .Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias).Append(" AS T1, ")
                .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).AppendLine(" AS Sid1, T2, Sid2")
                .Append("FROM ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
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

        private sealed class TargetFirstChainedSearchState
        {
            public TargetFirstChainedSearchState(
                SqlChainLinkExpression chainExpression,
                SearchParamTableExpression targetExpression,
                SearchParamTableExpression sourceDateExpression)
            {
                ChainExpression = chainExpression;
                TargetExpression = targetExpression;
                SourceDateExpression = sourceDateExpression;
            }

            public SqlChainLinkExpression ChainExpression { get; }

            public SearchParamTableExpression TargetExpression { get; }

            public SearchParamTableExpression SourceDateExpression { get; }

            public int SourceCteId { get; set; } = -1;
        }
    }
}
