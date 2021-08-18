// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ProgressRecord
    {
        public ProgressRecord()
        {
        }

        public ProgressRecord(long lastSurrogatedId)
        {
            LastSurrogatedId = lastSurrogatedId;
        }

        public ProgressRecord(long lastSurrogatedId, long successResourceCount, long failResourceCount)
        {
            LastSurrogatedId = lastSurrogatedId;
            SuccessResourceCount = successResourceCount;
            FailResourceCount = failResourceCount;
        }

        public long LastSurrogatedId { get; set; }

        public long SuccessResourceCount { get; set; }

        public long FailResourceCount { get; set; }
    }
}
