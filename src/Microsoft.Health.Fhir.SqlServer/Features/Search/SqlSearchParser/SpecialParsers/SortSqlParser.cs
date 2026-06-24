// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SortSqlParser : ISqlParser
    {
        private readonly SearchParameterCollection _parameterCollection;
        private readonly Dictionary<string, ISqlParser> _sqlParsers;

        public SortSqlParser(SearchParameterCollection parameterCollection, Dictionary<string, ISqlParser> sqlParsers)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(sqlParsers);

            _parameterCollection = parameterCollection;
            _sqlParsers = sqlParsers;
        }

        // This still need work
        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var parts = value.Split(':', 2);
            var parameter = _parameterCollection.GetByCode(parts[1], options.ResourceTypes[0]);

            if (parameter == null)
            {
                return null;
            }

            if (!_sqlParsers.TryGetValue(parameter.Type, out var parser))
            {
                return null;
            }

            options.Sort = true;

            return null;
        }
    }
}
