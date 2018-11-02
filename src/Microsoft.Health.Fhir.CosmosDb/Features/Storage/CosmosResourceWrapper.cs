// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal class CosmosResourceWrapper : ResourceWrapper, ISupportSearchIndices
    {
        public CosmosResourceWrapper(ResourceWrapper resource)
            : this(
                  IsNotNull(resource).ResourceId,
                  resource.Version,
                  resource.ResourceTypeName,
                  resource.RawResource,
                  resource.Request,
                  resource.LastModified,
                  resource.IsDeleted,
                  resource.IsHistory,
                  (resource as ISupportSearchIndices)?.SearchIndices,
                  resource.LastModifiedClaims)
        {
        }

        public CosmosResourceWrapper(
            string resourceId,
            string versionId,
            string resourceTypeName,
            RawResource rawResource,
            ResourceRequest request,
            DateTimeOffset lastModified,
            bool deleted,
            bool history,
            IReadOnlyCollection<SearchIndexEntry> searchIndices,
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims)
            : base(resourceId, versionId, resourceTypeName, rawResource, request, lastModified, deleted, lastModifiedClaims)
        {
            IsHistory = history;
            SearchIndices = searchIndices ?? Array.Empty<SearchIndexEntry>();
        }

        [JsonConstructor]
        protected CosmosResourceWrapper()
        {
        }

        [JsonProperty("id")]
        public string Id
        {
            get
            {
                if (IsHistory)
                {
                    return $"{ResourceId}_{GetETagOrVersion()}";
                }

                return ResourceId;
            }
        }

        [JsonProperty("_etag")]
        public string ETag { get; protected set; }

        [JsonProperty("version")]
        public override string Version
        {
            get => GetETagOrVersion();
            set => base.Version = value;
        }

        [JsonProperty(KnownResourceWrapperProperties.SearchIndices, ItemConverterType = typeof(SearchIndexEntryConverter))]
        public IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; }

        [JsonProperty("partitionKey")]
        public string PartitionKey => ToResourceKey().ToPartitionKey();

        internal string GetETagOrVersion()
        {
            // An ETag is used as the Version when the Version property is not specified
            // This occurs on the master resource record
            if (string.IsNullOrEmpty(base.Version) && !string.IsNullOrEmpty(ETag))
            {
                return ETag.Trim('"');
            }

            return base.Version;
        }

        private static ResourceWrapper IsNotNull(ResourceWrapper resource)
        {
            EnsureArg.IsNotNull(resource);

            return resource;
        }
    }
}
