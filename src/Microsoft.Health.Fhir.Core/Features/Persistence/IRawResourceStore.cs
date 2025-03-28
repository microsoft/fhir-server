// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public interface IRawResourceStore
{
    /// <summary>
    /// Writes new raw FHIR resources to the store.
    /// </summary>
    /// <param name="rawResources">The raw resources to write.</param>
    /// <param name="storageIdentifier">Identifier to be used with the storing raw resources</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The identifier of the inserted resource.</returns>
    Task<IReadOnlyList<ResourceWrapper>> WriteRawResourcesAsync(IReadOnlyList<ResourceWrapper> rawResources, long storageIdentifier, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a list of FHIR resources from the store.
    /// </summary>
    /// <param name="rawResourceLocators">The raw resources to read, including storage identifier and offset.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A dictionary, where the key contains thestorage identifier and offset of the raw resource. Value is the raw resource.</returns>
    Task<Dictionary<RawResourceLocator, string>> ReadRawResourcesAsync(IReadOnlyList<RawResourceLocator> rawResourceLocators, CancellationToken cancellationToken);
}
