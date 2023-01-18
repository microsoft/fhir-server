// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement;

public static class JobInfoExtensions
{
    public static int? GetJobTypeId(this JobInfo jobInfo)
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        IJobData jobDataDefinition = JsonConvert.DeserializeObject<JobDataDefinition>(jobInfo.Definition);

        return jobDataDefinition?.TypeId;
    }

    [SuppressMessage("Code", "CA1812:Avoid uninstantiated internal classes", Justification = "Used by JsonConvert")]
    internal sealed class JobDataDefinition : IJobData
    {
        [JsonProperty("typeId")]
        public int TypeId { get; set; }
    }
}
