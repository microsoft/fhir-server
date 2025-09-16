// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Search.Caching
{
    /// <summary>
    /// Distributed cache interface specifically for search parameter status items
    /// </summary>
    public interface ISearchParameterCache : IDistributedCache<ResourceSearchParameterStatus>
    {
        // No additional methods needed - just a typed alias of the generic interface
    }
}
