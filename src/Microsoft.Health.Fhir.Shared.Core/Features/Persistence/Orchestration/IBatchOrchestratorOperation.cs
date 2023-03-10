// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public interface IBatchOrchestratorOperation<T>
        where T : class
    {
        DateTime CreationTime { get; }

        int CurrentExpectedNumberOfResources { get; }

        Guid Id { get; }

        string Label { get; }

        int OriginalExpectedNumberOfResources { get; }

        BatchOrchestratorOperationStatus Status { get; }

        BatchOrchestratorOperationType Type { get; }

        Task AppendResourceAsync(T resource, CancellationToken cancellationToken);

        Task ReleaseResourceAsync(string reason, CancellationToken cancellationToken);
    }
}
