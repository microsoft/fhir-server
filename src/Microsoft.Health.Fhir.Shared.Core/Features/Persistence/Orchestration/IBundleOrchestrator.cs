// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public interface IBundleOrchestrator<T>
        where T : class
    {
        IBundleOrchestratorOperation<T> CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources);

        bool RemoveOperation(Guid id);
    }
}
