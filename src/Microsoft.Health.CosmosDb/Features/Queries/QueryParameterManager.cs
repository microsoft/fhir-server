// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Documents;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    public class QueryParameterManager
    {
        private const string ParamPrefix = "param";

        private Dictionary<object, string> _parameterMapping = new Dictionary<object, string>();

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

        public SqlParameterCollection ToSqlParameterCollection()
        {
            return new SqlParameterCollection(_parameterMapping.Select(v => new SqlParameter(v.Value, v.Key)));
        }
    }
}
