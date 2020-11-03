// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal class FhirCosmosResourceWrapper : ResourceWrapper
    {
        public FhirCosmosResourceWrapper(ResourceWrapper resource)
            : this(
                  EnsureArg.IsNotNull(resource, nameof(resource)).ResourceId,
                  resource.Version,
                  resource.ResourceTypeName,
                  resource.RawResource,
                  resource.Request,
                  resource.LastModified,
                  resource.IsDeleted,
                  resource.IsHistory,
                  resource.SearchIndices,
                  resource.CompartmentIndices,
                  resource.LastModifiedClaims,
                  resource.SearchParameterHash)
        {
        }

        public FhirCosmosResourceWrapper(
            string resourceId,
            string versionId,
            string resourceTypeName,
            RawResource rawResource,
            ResourceRequest request,
            DateTimeOffset lastModified,
            bool deleted,
            bool history,
            IReadOnlyCollection<SearchIndexEntry> searchIndices,
            CompartmentIndices compartmentIndices,
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims,
            string searchParameterHash = null)
            : base(resourceId, versionId, resourceTypeName, rawResource, request, lastModified, deleted, searchIndices, compartmentIndices, lastModifiedClaims, searchParameterHash)
        {
            IsHistory = history;
        }

        [JsonConstructor]
        protected FhirCosmosResourceWrapper()
        {
        }

        [JsonProperty(KnownDocumentProperties.Id)]
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

        [JsonProperty(KnownDocumentProperties.ActivePeriodEndDateTime)]
        public DateTimeOffset? ActivePeriodEndDateTime { get; }

        [JsonProperty(KnownDocumentProperties.ETag)]
        public string ETag { get; protected set; }

        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = false;

        [JsonProperty("version")]
        public override string Version
        {
            get => GetETagOrVersion();
            set => base.Version = value;
        }

        [JsonProperty(KnownResourceWrapperProperties.SearchIndices, ItemConverterType = typeof(SearchIndexEntryConverter))]
        public override IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; protected set; }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey => ToResourceKey().ToPartitionKey();

        [JsonProperty("referencesToInclude")]
        public IReadOnlyList<ResourceToInclude> ReferencesToInclude { get; set; }

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

        public class ResourceToInclude : IEquatable<ResourceToInclude>
        {
            [JsonProperty(SearchValueConstants.ReferenceResourceTypeName)]
            public string ResourceTypeName { get; set; }

            [JsonProperty(SearchValueConstants.ReferenceResourceIdName)]
            public string ResourceId { get; set; }

            public bool Equals(ResourceToInclude other)
            {
                if (ReferenceEquals(null, other))
                {
                    return false;
                }

                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                return ResourceTypeName == other.ResourceTypeName && ResourceId == other.ResourceId;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as ResourceToInclude);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ResourceTypeName, ResourceId);
            }
        }
    }
}
