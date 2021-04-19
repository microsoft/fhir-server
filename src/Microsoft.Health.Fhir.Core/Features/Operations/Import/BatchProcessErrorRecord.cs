// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BatchProcessErrorRecord
    {
        public BatchProcessErrorRecord(IEnumerable<ProcessError> processErrors, long lastSurragatedId)
        {
            ProcessErrors = processErrors;
            LastSurragatedId = lastSurragatedId;
        }

        public IEnumerable<ProcessError> ProcessErrors { get; private set; }

        public long LastSurragatedId { get; set; }
    }
}
