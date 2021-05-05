// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorTaskInputData
    {
        public Uri RequestUri { get; set; }

        public string InputFormat { get; set; }

        public Uri InputSource { get; set; }

        public Uri BaseUri { get; set; }

        public string TaskId { get; set; }

        public IReadOnlyList<InputResource> Input { get; set; }

        public ImportRequestStorageDetail StorageDetail { get; set; }

        public int MaxConcurrentProcessingTaskCount { get; set; }

        public string ProcessingTaskQueueId { get; set; }
    }
}
