﻿// -------------------------------------------------------------------------------------------------
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
    /// Reads a raw FHIR resource from the store.
    /// </summary>
    /// <param name="storageIdentifier">Identifier to be used with the storing raw resources</param>
    /// <param name="offset">Raw Resource start position in the blob</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The raw resource</returns>
    Task<RawResource> ReadRawResourceAsync(long storageIdentifier, long offset, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a list of FHIR resources from the store.
    /// </summary>
    /// <param name="rawResources">The raw resources to read.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The list of the raw resources with containing data</returns>
    Task<IReadOnlyList<RawResource>> ReadRawResourcesAsync(IList<ResourceWrapper> rawResources, CancellationToken cancellationToken);
}
