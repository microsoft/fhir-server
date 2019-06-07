// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface IBundleFactory
    {
        ResourceElement CreateSearchBundle(SearchResult result);

        ResourceElement CreateHistoryBundle(SearchResult result);
    }
}
