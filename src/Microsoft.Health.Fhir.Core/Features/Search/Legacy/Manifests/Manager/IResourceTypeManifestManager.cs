// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    /// <summary>
    /// Provides a mechanism to manage <see cref="ResourceTypeManifest"/>.
    /// </summary>
    public interface IResourceTypeManifestManager
    {
        /// <summary>
        /// Gets a <see cref="ResourceTypeManifest"/> for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type whose <see cref="ResourceTypeManifest"/> should be retrieved.</param>
        /// <returns>An instance of <see cref="ResourceTypeManifest"/>.</returns>
        ResourceTypeManifest GetManifest(Type resourceType);

        /// <summary>
        /// Gets the common search properties for all Resource types
        /// </summary>
        ResourceTypeManifest GetGenericManifest();
    }
}
