// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Routing
{
    public interface IQueryStringParser
    {
        IReadOnlyCollection<KeyValuePair<string, string>> Parse(string queryString);
    }
}
