// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public interface IBundleOrchestrator
    {
        bool IsEnabled { get; }

        IBundleOrchestratorOperation CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources);

        IBundleOrchestratorOperation GetOperation(Guid operationId);

        bool CompleteOperation(IBundleOrchestratorOperation operation);
    }
}
