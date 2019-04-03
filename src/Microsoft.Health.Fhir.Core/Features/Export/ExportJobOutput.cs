// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobOutput
    {
        [JsonConstructor]
        public ExportJobOutput()
        {
        }

        [JsonProperty(JobRecordProperties.Error)]
        public List<ExportFileInfo> Errors { get; } = new List<ExportFileInfo>();

        [JsonProperty(JobRecordProperties.Result)]
        public List<ExportFileInfo> Results { get; } = new List<ExportFileInfo>();
    }
}
