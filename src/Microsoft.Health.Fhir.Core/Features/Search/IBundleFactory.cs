// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface IBundleFactory
    {
        ResourceElement CreateSearchBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParams, SearchResult result);

        ResourceElement CreateHistoryBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParams, SearchResult result);
    }
}
