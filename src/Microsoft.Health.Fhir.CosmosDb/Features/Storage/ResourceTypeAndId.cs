// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Represents a reference to another resource. <see cref="ResourceTypeName"/> is allowed to be null, making the reference untyped.
    /// </summary>
    internal readonly struct ResourceTypeAndId : IEquatable<ResourceTypeAndId>
    {
        [JsonConstructor]
        public ResourceTypeAndId(string resourceTypeName, string resourceId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            ResourceTypeName = resourceTypeName;
            ResourceId = resourceId;
        }

        [JsonProperty(SearchValueConstants.ReferenceResourceTypeName)]
        public string ResourceTypeName { get; }

        [JsonProperty(SearchValueConstants.ReferenceResourceIdName)]
        public string ResourceId { get; }

        public bool Equals(ResourceTypeAndId other) => ResourceTypeName == other.ResourceTypeName && ResourceId == other.ResourceId;

        public override bool Equals(object obj) => obj is ResourceTypeAndId rid && Equals(rid);

        public override int GetHashCode() => HashCode.Combine(ResourceTypeName, ResourceId);
    }
}
