// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    /// <summary>
    /// Represents a capability that provides canonical URLs for the 'instantiates' field of the CapabilityStatement.
    /// </summary>
    public interface IInstantiateCapability
    {
        /// <summary>
        /// Gets the collection of canonical URLs that this capability instantiates.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A collection of canonical URLs, or an empty collection if none are available.</returns>
        Task<ICollection<string>> GetCanonicalUrlsAsync(CancellationToken cancellationToken);
    }
}
