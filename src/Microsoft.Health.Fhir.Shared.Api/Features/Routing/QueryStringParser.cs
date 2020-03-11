// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    public class QueryStringParser : IQueryStringParser
    {
        public IReadOnlyCollection<KeyValuePair<string, string>> Parse(string queryString)
        {
            return QueryHelpers.ParseQuery(queryString)
                .SelectMany(
                    query => query.Value,
                    (query, value) => new KeyValuePair<string, string>(query.Key, value))
                .ToArray();
        }
    }
}
