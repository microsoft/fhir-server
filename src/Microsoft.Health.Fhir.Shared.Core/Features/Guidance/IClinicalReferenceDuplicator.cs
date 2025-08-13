// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Guidance
{
    public interface IClinicalReferenceDuplicator
    {
        Task<(ResourceWrapper source, ResourceWrapper duplicate)> CreateResourceAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceKey>> DeleteResourceAsync(
            ResourceKey resourceKey,
            DeleteOperation deleteOperation,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceWrapper>> SearchResourceAsync(
            string duplicateResourceType,
            string resourceId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<ResourceWrapper>> UpdateResourceAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken);

        bool IsDuplicatableResourceType(string resourceType);
    }
}
