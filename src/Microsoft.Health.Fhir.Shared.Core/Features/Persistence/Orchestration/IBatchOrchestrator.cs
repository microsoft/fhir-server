// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    internal interface IBatchOrchestrator<T>
        where T : class
    {
        BatchOrchestratorJob<T> CreateNewJob(string label, int expectedNumberOfResources);

        bool RemoveJob(Guid id);
    }
}
