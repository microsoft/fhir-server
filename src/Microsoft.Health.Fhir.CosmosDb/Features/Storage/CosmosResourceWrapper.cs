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
        public CosmosResourceWrapper(ResourceWrapper resource, int dataVersion)
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
                  resource.LastModifiedClaims,
                  dataVersion)
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
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims,
            int dataVersion)
            : base(resourceId, versionId, resourceTypeName, rawResource, request, lastModified, deleted, lastModifiedClaims)
        {
            IsHistory = history;
            DataVersion = dataVersion;
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
            protected set => base.Version = value;
        }

        [JsonProperty(KnownResourceWrapperProperties.SearchIndices, ItemConverterType = typeof(SearchIndexEntryConverter))]
        public IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; }

        [JsonProperty("partitionKey")]
        public string PartitionKey => ToResourceKey().ToPartitionKey();

        [JsonProperty(KnownResourceWrapperProperties.DataVersion)]
        public int DataVersion { get; protected set; }

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

        internal void UpdateDataVersion(int version)
        {
            DataVersion = version;
        }

        private static ResourceWrapper IsNotNull(ResourceWrapper resource)
        {
            EnsureArg.IsNotNull(resource);

            return resource;
        }
    }
}
