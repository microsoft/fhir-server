// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportFileInfo
    {
        public ExportFileInfo(
            string type,
            Uri fileUri,
            int sequence,
            int count,
            long committedBytes)
        {
            EnsureArg.IsNotNullOrEmpty(type);
            EnsureArg.IsNotNull(fileUri);

            Type = type;
            FileUri = fileUri;
            Sequence = sequence;
            Count = count;
            CommittedBytes = committedBytes;
        }

        [JsonConstructor]
        public ExportFileInfo()
        {
        }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; }

        [JsonProperty(JobRecordProperties.FileUri)]
        public Uri FileUri { get; }

        [JsonProperty(JobRecordProperties.Sequence)]
        public int Sequence { get; }

        [JsonProperty(JobRecordProperties.Count)]
        public int Count { get; }

        [JsonProperty(JobRecordProperties.CommitedBytes)]
        public long CommittedBytes { get; }
    }
}
