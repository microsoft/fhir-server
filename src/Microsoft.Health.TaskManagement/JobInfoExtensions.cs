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

        IJobData jobDataDefinition = JsonConvert.DeserializeObject<JobDataDefinition>(jobInfo.Definition);

        return jobDataDefinition?.TypeId;
    }

    public static void Report<T>(this IProgress<string> progress, T result)
    {
        EnsureArg.IsNotNull(progress, nameof(progress));

        progress.Report(JsonConvert.SerializeObject(result));
    }

    public static T DeserializeResult<T>(this JobInfo jobInfo)
        where T : new()
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        if (!string.IsNullOrEmpty(jobInfo.Result))
        {
            T jobDataDefinition = JsonConvert.DeserializeObject<T>(jobInfo.Result);
            return jobDataDefinition;
        }

        return new T();
    }

    public static T DeserializeDefinition<T>(this JobInfo jobInfo)
        where T : IJobData
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        T jobDataDefinition = JsonConvert.DeserializeObject<T>(jobInfo.Definition);

        return jobDataDefinition;
    }

    public static JobInfo SerializeDefinition<T>(this JobInfo jobInfo, T definition)
        where T : IJobData
    {
        EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

        jobInfo.Definition = JsonConvert.SerializeObject(definition);

        return jobInfo;
    }

    [SuppressMessage("Code", "CA1812:Avoid uninstantiated internal classes", Justification = "Used by JsonConvert")]
    internal sealed class JobDataDefinition : IJobData
    {
        [JsonProperty("typeId")]
        public int TypeId { get; set; }
    }
}
