// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class ResourceChangeData
    {
        public ResourceChangeData(long id, DateTime timestamp, string resourceId, short resourceTypeId, int resourceVersion, byte resourceChangeTypeId)
        {
            Id = id;
            Timestamp = timestamp;
            ResourceId = resourceId;
            ResourceTypeId = resourceTypeId;
            ResourceVersion = resourceVersion;
            ResourceChangeTypeId = resourceChangeTypeId;
        }

        public long Id { get; init; }

        public string ResourceId { get; init; }

        public short ResourceTypeId { get; init; }

        public int ResourceVersion { get; init; }

        public byte ResourceChangeTypeId { get; init; }

        public DateTime Timestamp { get; init; }
    }
}
