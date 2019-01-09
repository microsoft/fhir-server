// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    public class QueryHelper
    {
        private readonly StringBuilder _queryBuilder;
        private readonly QueryParameterManager _queryParameterManager;
        private readonly string _rootAliasName;

        public QueryHelper(StringBuilder queryBuilder, QueryParameterManager queryParameterManager, string rootAliasName)
        {
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(queryParameterManager, nameof(queryParameterManager));
            EnsureArg.IsNotEmptyOrWhitespace(rootAliasName, nameof(queryParameterManager));

            _queryBuilder = queryBuilder;
            _queryParameterManager = queryParameterManager;
            _rootAliasName = rootAliasName;
        }

        public void AppendSelectFromRoot(string selectList)
        {
            _queryBuilder
                .Append("SELECT ")
                .Append(selectList)
                .Append(" FROM root ")
                .AppendLine(_rootAliasName);
        }

        public void AppendFilterCondition(string logicalOperator, params (string, object)[] conditions)
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                _queryBuilder
                    .Append(logicalOperator)
                    .Append(" ");

                (string name, object value) = conditions[i];

                AppendFilterCondition(name, value);
            }
        }

        public void AppendFilterCondition(string name, object value)
        {
            _queryBuilder
                    .Append(_rootAliasName).Append(".").Append(name)
                    .Append(" = ")
                    .AppendLine(_queryParameterManager.AddOrGetParameterMapping(value));
        }

        public void AppendSystemDataFilter(bool systemDataValue = false)
        {
            _queryBuilder
                .Append(" WHERE ")
                .Append(_rootAliasName).Append(".isSystem")
                .Append(" = ")
                .AppendLine(_queryParameterManager.AddOrGetParameterMapping(systemDataValue));
        }
    }
}
