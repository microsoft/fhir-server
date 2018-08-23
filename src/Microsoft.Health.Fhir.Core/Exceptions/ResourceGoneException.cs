// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class ResourceGoneException : FhirException
    {
        public ResourceGoneException(ResourceKey deletedResource)
        {
            EnsureArg.IsNotNull(deletedResource);

            DeletedResource = deletedResource;
        }

        public ResourceKey DeletedResource { get; }
    }
}
