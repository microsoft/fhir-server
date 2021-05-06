// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorTaskContext
    {
#pragma warning disable CA2227 // Collection properties should be read only
        public IDictionary<Uri, TaskInfo> DataProcessingTasks { get; set; } = new Dictionary<Uri, TaskInfo>();
#pragma warning restore CA2227 // Collection properties should be read only

        public ImportOrchestratorTaskProgress Progress { get; set; }
    }
}
