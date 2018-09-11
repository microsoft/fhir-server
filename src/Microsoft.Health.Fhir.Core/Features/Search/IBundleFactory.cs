// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface IBundleFactory
    {
        Bundle CreateSearchBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParams, SearchResult result);

        Bundle CreateHistoryBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParams, SearchResult result);
    }
}
