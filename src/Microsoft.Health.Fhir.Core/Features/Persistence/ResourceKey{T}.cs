// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceKey<T> : ResourceKey
        where T : Resource
    {
        public ResourceKey(string id, string versionId = null)
            : base(ModelInfo.GetFhirTypeNameForType(typeof(T)), id, versionId)
        {
        }
    }
}
