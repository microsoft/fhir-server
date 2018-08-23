// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Resource wrapper with search indices.
    /// </summary>
    internal class ResourceWrapperWithSearchIndices : ResourceWrapper, ISupportSearchIndices
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceWrapperWithSearchIndices"/> class.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="rawResource">The raw resource.</param>
        /// <param name="request">Request information for how th resource was made.</param>
        /// <param name="isDeleted">A flag indicating whether the source is deleted or not.</param>
        /// <param name="searchIndices">The search indices.</param>
        /// <param name="lastModifiedClaims">The security claims when the resource was last modified.</param>
        public ResourceWrapperWithSearchIndices(
            Resource resource,
            RawResource rawResource,
            ResourceRequest request,
            bool isDeleted,
            IReadOnlyCollection<SearchIndexEntry> searchIndices,
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims)
            : base(resource, rawResource, request, isDeleted, lastModifiedClaims)
        {
            SearchIndices = searchIndices ?? System.Array.Empty<SearchIndexEntry>();
        }

        /// <inheritdoc />
        public IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; }
    }
}
