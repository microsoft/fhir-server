// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class ResourceChangeData
    {
        public ResourceChangeData(long id, DateTime timestamp, string resourceId, short resourceTypeId, int resourceVersion, byte resourceChangeTypeId, string resourceTypeName)
        {
            EnsureArg.IsNotNullOrEmpty(resourceId, nameof(resourceId));
            EnsureArg.IsNotNullOrEmpty(resourceTypeName, nameof(resourceTypeName));

            Id = id;
            Timestamp = timestamp;
            ResourceId = resourceId;
            ResourceTypeId = resourceTypeId;
            ResourceVersion = resourceVersion;
            ResourceChangeTypeId = resourceChangeTypeId;
            ResourceTypeName = resourceTypeName;
        }

        public long Id { get; }

        public string ResourceId { get; }

        public short ResourceTypeId { get; }

        public int ResourceVersion { get; }

        public byte ResourceChangeTypeId { get; }

        public DateTime Timestamp { get; }

        public string ResourceTypeName { get; }
    }
}
