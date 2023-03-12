// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;

public static class JobInfoExtensions
{
    public static IEnumerable<JobInfo> ToJobInfo(this JobGroupWrapper jobGroupWrapper)
    {
        return jobGroupWrapper.ToJobInfo(jobGroupWrapper.Definitions);
    }

    public static IEnumerable<JobInfo> ToJobInfo(this JobGroupWrapper jobGroupWrapper, IEnumerable<JobDefinitionWrapper> items)
    {
        return items.Select(jobInfoWrapperItem => new JobInfo
        {
            Id = long.Parse(jobInfoWrapperItem.JobId),
            QueueType = jobGroupWrapper.QueueType,
            Status = jobInfoWrapperItem.Status.HasValue ? (JobStatus?)jobInfoWrapperItem.Status.Value : null,
            GroupId = long.Parse(jobGroupWrapper.GroupId),
            Definition = jobInfoWrapperItem.Definition,
            Result = jobInfoWrapperItem.Result,
            Data = jobInfoWrapperItem.Data,
            CancelRequested = jobInfoWrapperItem.CancelRequested,
            Priority = jobGroupWrapper.Priority,
            CreateDate = jobGroupWrapper.CreateDate.UtcDateTime,
            StartDate = jobInfoWrapperItem.StartDate?.UtcDateTime,
            EndDate = jobInfoWrapperItem.EndDate?.UtcDateTime,
            HeartbeatDateTime = jobInfoWrapperItem.HeartbeatDateTime.UtcDateTime,
            Version = jobInfoWrapperItem.Version,
        });
    }
}
