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
        public List<ExportJobOutputComponent> Errors { get; } = new List<ExportJobOutputComponent>();

        [JsonProperty(JobRecordProperties.Result)]
        public List<ExportJobOutputComponent> Results { get; } = new List<ExportJobOutputComponent>();
    }
}
