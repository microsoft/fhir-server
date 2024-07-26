// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.InMemory
{
    public class InMemoryIndex
    {
        private readonly ISearchIndexer _searchIndexer;

        public InMemoryIndex(ISearchIndexer searchIndexer)
        {
            _searchIndexer = EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            Index = new ConcurrentDictionary<string, List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>>();
        }

        internal ConcurrentDictionary<string, List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)>> Index
        {
            get;
        }

        public void IndexResources(params ResourceElement[] resources)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            foreach (var resource in resources)
            {
                var indexEntries = _searchIndexer.Extract(resource);

                Index.AddOrUpdate(
                    resource.InstanceType,
                    key => new List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)> { (ToResourceKey(resource), indexEntries) },
                    (key, list) =>
                    {
                        list.Add((ToResourceKey(resource), indexEntries));
                        return list;
                    });
            }
        }

        private static ResourceKey ToResourceKey(ResourceElement resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return new ResourceKey(resource.InstanceType, resource.Id, resource.VersionId);
        }
    }
}
