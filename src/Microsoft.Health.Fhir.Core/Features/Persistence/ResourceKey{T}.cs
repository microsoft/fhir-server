// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceKey<T> : ResourceKey
    {
        public ResourceKey(string id, string versionId = null)
            : base(ModelInfoProvider.GetFhirTypeNameForType(typeof(T)), id, versionId)
        {
        }
    }
}
