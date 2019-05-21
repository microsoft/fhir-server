// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceKey
    {
        public ResourceKey(string resourceType, string id, string versionId = null)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));

            Id = id;
            VersionId = versionId;
            ResourceType = resourceType;
        }

        public string Id { get; }

        public string VersionId { get; }

        public string ResourceType { get; }
    }
}
