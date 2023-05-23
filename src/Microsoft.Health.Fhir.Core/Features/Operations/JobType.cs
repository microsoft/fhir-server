// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public enum JobType : int
    {
        Unknown = 0, // should not be used
        ImportProcessing = 1,
        ImportOrchestrator = 2,
        ExportProcessing = 3,
        ExportOrchestrator = 4,
        ReindexOrchestrator = 5,
    }
}
