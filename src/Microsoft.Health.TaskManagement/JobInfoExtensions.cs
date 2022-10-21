// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement;

public static class JobInfoExtensions
{
    public static int? GetJobTypeId(this JobInfo jobInfo)
    {
        IJobInfo jobInfoDefinition = JsonConvert.DeserializeObject<JobInfoDefinition>(jobInfo.Definition);

        return jobInfoDefinition?.TypeId;
    }

    [SuppressMessage("Code", "CA1812:Avoid uninstantiated internal classes", Justification = "Used by JsonConvert")]
    internal class JobInfoDefinition : IJobInfo
    {
        [JsonProperty("typeId")]
        public int TypeId { get; set; }
    }
}
