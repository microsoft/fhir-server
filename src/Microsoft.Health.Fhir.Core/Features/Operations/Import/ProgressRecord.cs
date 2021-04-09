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

        public long LastSurrogatedId { get; set; }

        public long LastOffset { get; set; }
    }
}
