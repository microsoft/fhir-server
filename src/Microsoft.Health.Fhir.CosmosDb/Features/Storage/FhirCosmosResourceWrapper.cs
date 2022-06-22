// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
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

            UpdateSortIndex(searchIndices);
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
        public DateTimeOffset? ActivePeriodEndDateTime { get; set; }

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
        public override IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; set; }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey => ToResourceKey().ToPartitionKey();

        [JsonProperty(KnownDocumentProperties.ReferencesToInclude)]
        public IReadOnlyList<ResourceTypeAndId> ReferencesToInclude { get; set; }

        [JsonProperty("sort", ItemConverterType = typeof(SortEntryConverter))]
        public IDictionary<string, SortValue> SortValues { get; set; }

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

        public override void UpdateSearchIndices(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            base.UpdateSearchIndices(searchIndices);

            UpdateSortIndex(searchIndices);
        }

        private void UpdateSortIndex(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            SortValues = new Dictionary<string, SortValue>();

            if (searchIndices == null)
            {
                return;
            }

            foreach (SearchIndexEntry entry in searchIndices)
            {
                if (entry.Value is ISupportSortSearchValue sortValue && (sortValue.IsMax || sortValue.IsMin))
                {
                    if (SortValues.TryGetValue(entry.SearchParameter.Code, out SortValue item))
                    {
                        SetSortValue(item, sortValue, entry);
                    }
                    else
                    {
                        var sortValueEntry = new SortValue(entry.SearchParameter.Url);
                        SetSortValue(sortValueEntry, sortValue, entry);
                        SortValues[entry.SearchParameter.Code] = sortValueEntry;
                    }
                }
            }

            void SetSortValue(SortValue item, ISupportSortSearchValue sortValue, SearchIndexEntry entry)
            {
                if (sortValue.IsMax)
                {
                    item.High = entry.Value;
                }

                if (sortValue.IsMin)
                {
                    item.Low = entry.Value;
                }
            }
        }
    }
}
