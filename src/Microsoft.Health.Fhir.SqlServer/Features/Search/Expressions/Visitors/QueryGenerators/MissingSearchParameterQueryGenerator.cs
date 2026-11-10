// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal sealed class MissingSearchParameterQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        private readonly Table _searchParameterTable;

        public MissingSearchParameterQueryGenerator(Table searchParameterTable)
        {
            EnsureArg.IsNotNull(searchParameterTable, nameof(searchParameterTable));
            _searchParameterTable = searchParameterTable;
        }

        public override Table Table => VLatest.Resource;

        public override SearchParameterQueryGeneratorContext VisitMissingSearchParameter(
            MissingSearchParameterExpression expression,
            SearchParameterQueryGeneratorContext context)
        {
            var builder = context.StringBuilder;
            var searchParamId = context.Model.GetSearchParamId(expression.Parameter.Url);
            var searchParamIdColumn = VLatest.SearchParam.SearchParamId;

            builder.AppendLine("NOT EXISTS (");
            using (builder.Indent())
            {
                builder.AppendLine("SELECT 1")
                    .Append("FROM (SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS IndexedResourceTypeId, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS IndexedResourceSurrogateId, ")
                    .Append(searchParamIdColumn, null).Append(" FROM ").Append(_searchParameterTable)
                    .AppendLine(") missingIndex")
                    .Append("WHERE missingIndex.IndexedResourceTypeId = ").Append(VLatest.Resource.ResourceTypeId, context.TableAlias).AppendLine()
                    .Append("AND missingIndex.IndexedResourceSurrogateId = ").Append(VLatest.Resource.ResourceSurrogateId, context.TableAlias).AppendLine()
                    .Append("AND ").Append(searchParamIdColumn, "missingIndex").Append(" = ")
                    .Append(context.Parameters.AddParameter(searchParamIdColumn, searchParamId, true)).AppendLine();
            }

            builder.AppendLine(")");
            return context;
        }

        public override bool Equals(object obj) =>
            obj is MissingSearchParameterQueryGenerator other && _searchParameterTable.Equals(other._searchParameterTable);

        public override int GetHashCode() => _searchParameterTable.GetHashCode();
    }
}
