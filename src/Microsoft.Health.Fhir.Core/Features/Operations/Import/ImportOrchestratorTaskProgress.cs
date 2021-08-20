﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Import orchestrator task progress enums.
    /// </summary>
    public enum ImportOrchestratorTaskProgress
    {
        Initialized,
        InputResourcesValidated,
        PreprocessCompleted,
        SubTaskRecordsGenerated,
        SubTasksCompleted,
        PostprocessCompleted,
    }
}
