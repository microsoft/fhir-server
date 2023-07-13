// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement;

public static class JobInfoExtensions
{
    public static int? GetJobTypeId(this JobInfo jobInfo)
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        try
        {
            IJobData jobDataDefinition = JsonConvert.DeserializeObject<JobDataDefinition>(jobInfo.Definition);

            return jobDataDefinition?.TypeId;
        }
        catch (Exception ex)
        {
            jobInfo.Result = ex.Message;
            throw;
        }
    }

    public static T DeserializeDefinition<T>(this JobInfo jobInfo)
        where T : IJobData
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        if (jobInfo.Definition == null)
        {
            return default;
        }

        return JsonConvert.DeserializeObject<T>(jobInfo.Definition);
    }

    public static T DeserializeResult<T>(this JobInfo jobInfo)
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        if (jobInfo.Result == null)
        {
            return default;
        }

        return JsonConvert.DeserializeObject<T>(jobInfo.Result);
    }

    [SuppressMessage("Code", "CA1812:Avoid uninstantiated internal classes", Justification = "Used by JsonConvert")]
    internal sealed class JobDataDefinition : IJobData
    {
        [JsonProperty("typeId")]
        public int TypeId { get; set; }
    }
}
