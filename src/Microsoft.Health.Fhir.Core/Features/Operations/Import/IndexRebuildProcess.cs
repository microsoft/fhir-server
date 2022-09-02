// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class IndexRebuildProcess
    {
        public IndexRebuildProcessingProgress IndexRebuildProcessingProgress { get; set; }

        public Dictionary<string, int> AlreadyCompletePartitionIds { get; } = new Dictionary<string, int>();
    }
}
