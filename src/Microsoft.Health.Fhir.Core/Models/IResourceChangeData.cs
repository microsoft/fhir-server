// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Models
{
    public interface IResourceChangeData
    {
        long Id { get; }

        string ResourceId { get; }

        short ResourceTypeId { get; }

        int ResourceVersion { get; }

        byte ResourceChangeTypeId { get; }

        DateTime Timestamp { get; }
    }
}
