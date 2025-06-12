// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Storage
{
    public enum FhirMemoryCacheItemPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        NeverRemove = 3,
    }
}
