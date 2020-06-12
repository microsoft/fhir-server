// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    public class QueryParameterManager
    {
        private const string ParamPrefix = "param";

        private readonly Dictionary<object, string> _parameterMapping = new Dictionary<object, string>();

        public string AddOrGetParameterMapping(object value)
        {
            if (_parameterMapping.TryGetValue(value, out string name))
            {
                return name;
            }

            name = $"@{ParamPrefix}{_parameterMapping.Count}";

            _parameterMapping.Add(value, name);

            return name;
        }

        public void AddToQuery(QueryDefinition query)
        {
            foreach ((string Key, object Value) item in ToSqlParameterCollection())
            {
                query.WithParameter(item.Key, item.Value);
            }
        }

        public IEnumerable<(string Key, object Value)> ToSqlParameterCollection()
        {
            return _parameterMapping.Select(v => (v.Value, v.Key));
        }
    }
}
