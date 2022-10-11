// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Export orchestrator job progress enums.
    /// </summary>
    public enum ExportOrchestratorJobProgress
    {
        Initialized,
        InputResourcesValidated,
        PreprocessCompleted,
        SubJobRecordsGenerated,
        SubJobsCompleted,
        PostprocessCompleted,
    }
}
