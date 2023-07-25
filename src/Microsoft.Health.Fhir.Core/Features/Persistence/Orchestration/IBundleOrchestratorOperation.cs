// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public interface IBundleOrchestratorOperation
    {
        DateTime CreationTime { get; }

        int CurrentExpectedNumberOfResources { get; }

        Guid Id { get; }

        string Label { get; }

        int OriginalExpectedNumberOfResources { get; }

        BundleOrchestratorOperationStatus Status { get; }

        BundleOrchestratorOperationType Type { get; }

        Task<UpsertOutcome> AppendResourceAsync(ResourceWrapperOperation resource, IFhirDataStore dataStore, CancellationToken cancellationToken);

        Task ReleaseResourceAsync(string reason, CancellationToken cancellationToken);

        void Cancel(string reason);
    }
}
