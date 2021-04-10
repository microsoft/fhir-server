// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.BackgroundTaskService
{
    public static class TaskTypeIds
    {
        public const short Unknown = 0;

        public const short BulkImportOrchestratorTask = 1;
        public const short BulkImportDataProcessingTask = 2;
    }
}
