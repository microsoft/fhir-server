// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace SMARTProxy.Models
{
    public class PipelineError
    {
        public PipelineError(string error, string description, DateTime timestamp, string stageId)
        {
            Error = error;
            ErrorDescription = description;
            Timestamp = timestamp;
            StageId = stageId;
        }

        public string Error { get; }

        public string ErrorDescription { get; }

        public DateTime Timestamp { get; }

        public string StageId { get; }

        public string ToContentString() => JsonSerializer.Serialize(this);
    }
}
