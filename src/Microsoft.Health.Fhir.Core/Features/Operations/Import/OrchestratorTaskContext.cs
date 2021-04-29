// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class OrchestratorTaskContext
    {
        public Dictionary<string, OrchestratorSubTaskRecord> SubTaskRecords { get; private set; } = new Dictionary<string, OrchestratorSubTaskRecord>();

        public OrchestratorTaskProgress Progress { get; set; }
    }
}
