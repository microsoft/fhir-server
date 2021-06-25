// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class ResourceChangeData : IResourceChangeData
    {
        public long Id { get; set; }

        public string ResourceId { get; set; }

        public short ResourceTypeId { get; set; }

        public int ResourceVersion { get; set; }

        public byte ResourceChangeTypeId { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
