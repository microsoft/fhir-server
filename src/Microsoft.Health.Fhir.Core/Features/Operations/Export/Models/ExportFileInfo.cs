// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Represents metadata required for each file that is generated as part of the
    /// export operation.
    /// </summary>
    public class ExportFileInfo
    {
        public ExportFileInfo(
            string type,
            Uri fileUri,
            int sequence)
        {
            EnsureArg.IsNotNullOrWhiteSpace(type);
            EnsureArg.IsNotNull(fileUri);

            Type = type;
            FileUri = fileUri;
            Sequence = sequence;
        }

        [JsonConstructor]
        protected ExportFileInfo()
        {
        }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; private set; }

        [JsonProperty(JobRecordProperties.Url)]
        public Uri FileUri { get; set; }

        [JsonProperty(JobRecordProperties.Sequence)]
        public int Sequence { get; private set; }

        [JsonProperty(JobRecordProperties.Count)]
        public int Count { get; private set; }

        [JsonProperty(JobRecordProperties.CommitedBytes)]
        public long CommittedBytes { get; private set; }

        public void IncrementCount(int numberOfBytes)
        {
            Count++;
            CommittedBytes += numberOfBytes;
        }

        public ExportOutputResponse ToExportOutputResponse()
        {
            return new ExportOutputResponse(Type, FileUri, Count);
        }
    }
}
