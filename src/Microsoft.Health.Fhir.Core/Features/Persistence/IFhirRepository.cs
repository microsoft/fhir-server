// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IFhirRepository
    {
        Task<Resource> CreateAsync(
            Resource resource,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<SaveOutcome> UpsertAsync(
            Resource resource,
            WeakETag weakETag = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Resource> GetAsync(
            ResourceKey key,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<ResourceKey> DeleteAsync(
            ResourceKey key,
            bool hardDelete,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
