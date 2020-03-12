// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class TestQueryStringParser : IQueryStringParser
    {
        public IReadOnlyCollection<KeyValuePair<string, string>> Parse(string queryString)
        {
            return queryString
                ?.Split('&')
                .Select(x =>
                {
                    string[] values = x.Split('=');
                    return new KeyValuePair<string, string>(values[0], values[1]);
                })
                .ToArray();
        }
    }
}
