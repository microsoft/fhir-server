// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Queries
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
            EnsureArg.IsNotNullOrWhiteSpace(rootAliasName, nameof(queryParameterManager));

            _queryBuilder = queryBuilder;
            _queryParameterManager = queryParameterManager;
            _rootAliasName = rootAliasName;
        }

        public void AppendSelect(string selectList)
        {
            _queryBuilder
                .Append("SELECT ")
                .Append(selectList);
        }

        public void AppendFromRoot()
        {
            _queryBuilder
                .AppendLine()
                .Append("FROM root ")
                .AppendLine(_rootAliasName);
        }

        public void AppendFilterCondition(string logicalOperator, bool equal, params (string, object)[] conditions)
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                _queryBuilder
                    .Append(logicalOperator)
                    .Append(" ");

                (string name, object value) = conditions[i];

                AppendFilterCondition(name, value, equal);
            }
        }

        public void AppendLiteralFilterCondition(string logicalOperator, bool equal, params (string, object)[] conditions)
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                _queryBuilder
                    .Append(logicalOperator)
                    .Append(" ");

                (string name, object value) = conditions[i];

                AppendLiteralFilterCondition(name, value, equal);
            }
        }

        public void AppendFilterCondition(string name, object value, bool equal)
        {
            string comparison = equal ? " = " : " != ";
            _queryBuilder
                .Append(_rootAliasName).Append(".").Append(name)
                .Append(comparison)
                .AppendLine(_queryParameterManager.AddOrGetParameterMapping(value));
        }

        public void AppendLiteralFilterCondition(string name, object value, bool equal)
        {
            string comparison = equal ? " = " : " != ";
            _queryBuilder
                .Append(_rootAliasName).Append(".").Append(name)
                .Append(comparison);

            AppendLiteral(value).AppendLine();
        }

        private StringBuilder AppendLiteral(object value)
        {
            return value switch
            {
                null => _queryBuilder.Append("null"),
                bool b => AppendLiteral(b),
                string s when Regex.IsMatch(s, @"^[A-Za-z_0-9]*$") => _queryBuilder.Append('\'').Append(s).Append('\''),
                string => throw new InvalidOperationException("Not expecting string that might need escaping"),
                _ => throw new InvalidOperationException($"Unexpected datatype {value.GetType().Name}"),
            };
        }

        private StringBuilder AppendLiteral(bool value)
        {
            return _queryBuilder.Append(value ? "true" : "false");
        }

        public void AppendSystemDataFilter(bool systemDataValue = false)
        {
            _queryBuilder
                .Append("WHERE ")
                .Append(_rootAliasName).Append(".isSystem")
                .Append(" = ");

            AppendLiteral(systemDataValue).AppendLine();
        }

        public void AppendSearchParameterHashFliter(string hashValue)
        {
            _queryBuilder
                .Append("AND")
                .Append(" (")
                .Append(_rootAliasName).Append(".")
                .Append(KnownResourceWrapperProperties.SearchParameterHash)
                .Append(" != ")
                .Append(_queryParameterManager.AddOrGetParameterMapping(hashValue))
                .Append(" OR IS_DEFINED(")
                .Append(_rootAliasName).Append(".")
                .Append(KnownResourceWrapperProperties.SearchParameterHash)
                .Append(") = ")
                .Append(_queryParameterManager.AddOrGetParameterMapping(false))
                .Append(")");
        }
    }
}
