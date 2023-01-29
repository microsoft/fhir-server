// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceWrapper
    {
        public ResourceWrapper(
            ResourceElement resource,
            RawResource rawResource,
            ResourceRequest request,
            bool deleted,
            IReadOnlyCollection<SearchIndexEntry> searchIndices,
            CompartmentIndices compartmentIndices,
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims,
            string searchParameterHash = null)
           : this(
                 EnsureArg.IsNotNull(resource).Id,
                 resource.VersionId,
                 resource.InstanceType,
                 rawResource,
                 request,
                 resource.LastUpdated ?? Clock.UtcNow,
                 deleted,
                 searchIndices,
                 compartmentIndices,
                 lastModifiedClaims,
                 searchParameterHash)
        {
        }

        public ResourceWrapper(
            string resourceId,
            string versionId,
            string resourceTypeName,
            RawResource rawResource,
            ResourceRequest request,
            DateTimeOffset lastModified,
            bool deleted,
            IReadOnlyCollection<SearchIndexEntry> searchIndices,
            CompartmentIndices compartmentIndices,
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims,
            string searchParameterHash = null)
        {
            EnsureArg.IsNotNullOrEmpty(resourceId, nameof(resourceId));
            EnsureArg.IsNotNullOrEmpty(resourceTypeName, nameof(resourceTypeName));
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            ResourceId = resourceId;
            Version = versionId;
            ResourceTypeName = resourceTypeName;
            RawResource = rawResource;
            Request = request;
            IsDeleted = deleted;
            LastModified = lastModified;
            SearchIndices = searchIndices;
            CompartmentIndices = compartmentIndices;
            LastModifiedClaims = lastModifiedClaims;
            SearchParameterHash = searchParameterHash;
        }

        [JsonConstructor]
        protected ResourceWrapper()
        {
        }

        [JsonProperty(KnownResourceWrapperProperties.LastModified)]
        public DateTimeOffset LastModified { get; internal set; }

        [JsonProperty(KnownResourceWrapperProperties.RawResource)]
        public RawResource RawResource { get; internal set; }

        [JsonProperty(KnownResourceWrapperProperties.Request)]
        public ResourceRequest Request { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.IsDeleted)]
        public bool IsDeleted { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.ResourceId)]
        public string ResourceId { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.ResourceTypeName)]
        public string ResourceTypeName { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.Version)]
        public virtual string Version { get; set; }

        [JsonProperty(KnownResourceWrapperProperties.IsHistory)]
        public virtual bool IsHistory { get; set; }

        [JsonProperty(KnownResourceWrapperProperties.SearchIndices)]
        public virtual IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; set; }

        [JsonProperty(KnownResourceWrapperProperties.LastModifiedClaims)]
        public IReadOnlyCollection<KeyValuePair<string, string>> LastModifiedClaims { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.CompartmentIndices)]
        public CompartmentIndices CompartmentIndices { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.SearchParameterHash)]
        public string SearchParameterHash { get; set; }

        public ResourceKey ToResourceKey(bool ignoreVersion = false)
        {
            return new ResourceKey(ResourceTypeName, ResourceId, ignoreVersion ? null : Version);
        }

        public virtual void UpdateSearchIndices(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            SearchIndices = searchIndices;
        }
    }
}
