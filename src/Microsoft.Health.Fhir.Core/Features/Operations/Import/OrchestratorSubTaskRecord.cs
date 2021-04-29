// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class OrchestratorSubTaskRecord
    {
        public string TaskId { get; set; }

        public bool IsCompletedSuccessfully { get; set; } = false;

        public string TaskInputData { get; set; }

        public int RetryCount { get; set; } = 0;
    }
}
