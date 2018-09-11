// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides a mechanism to create search indices.
    /// </summary>
    public class LegacySearchIndexer : ISearchIndexer
    {
        private readonly IResourceTypeManifestManager _resourceTypeManifestManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="LegacySearchIndexer"/> class.
        /// </summary>
        /// <param name="resourceTypeManifestManager">The resource type manifest manager.</param>
        public LegacySearchIndexer(IResourceTypeManifestManager resourceTypeManifestManager)
        {
            EnsureArg.IsNotNull(resourceTypeManifestManager, nameof(resourceTypeManifestManager));

            _resourceTypeManifestManager = resourceTypeManifestManager;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<SearchIndexEntry> Extract(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            List<SearchIndexEntry> entries = new List<SearchIndexEntry>();

            try
            {
                ResourceTypeManifest resourceManifest = _resourceTypeManifestManager.GetManifest(resource.GetType());

                foreach (SearchParam searchParam in resourceManifest.SupportedSearchParams)
                {
                    entries.AddRange(searchParam.ExtractValues(resource)
                        .Select(index => new SearchIndexEntry(searchParam.ParamName, index)));
                }
            }
            catch (ResourceNotSupportedException)
            {
                // We don't support extracting the search index from this resource type.
            }

            return entries;
        }
    }
}
