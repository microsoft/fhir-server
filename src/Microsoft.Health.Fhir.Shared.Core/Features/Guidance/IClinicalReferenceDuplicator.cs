// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Guidance
{
    public interface IClinicalReferenceDuplicator
    {
        Task<Resource> CreateResourceAsync(
            Resource resource,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceKey>> DeleteResourceAsync(
            ResourceKey resourceKey,
            DeleteOperation deleteOperation,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceWrapper>> SearchResourceAsync(
            string duplicateResourceType,
            string resourceId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<Resource>> UpdateResourceAsync(
            Resource resource,
            CancellationToken cancellationToken);

        bool CheckDuplicate(ResourceKey resourceKey);

        bool ShouldDuplicate(Resource resource);
    }
}
