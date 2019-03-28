// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobOutputComponent
    {
        public ExportJobOutputComponent(
            string type,
            int sequence,
            int count,
            long committedBytes)
        {
            EnsureArg.IsNotNullOrEmpty(type);

            Type = type;
            Sequence = sequence;
            Count = count;
            CommittedBytes = committedBytes;
        }

        [JsonConstructor]
        public ExportJobOutputComponent()
        {
        }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; }

        [JsonProperty(JobRecordProperties.Sequence)]
        public int Sequence { get; }

        [JsonProperty(JobRecordProperties.Count)]
        public int Count { get; }

        [JsonProperty(JobRecordProperties.CommitedBytes)]
        public long CommittedBytes { get; }
    }
}
