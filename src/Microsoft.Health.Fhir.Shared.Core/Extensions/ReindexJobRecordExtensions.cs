// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ReindexJobRecordExtensions
    {
        public static ResourceElement ToParametersResourceElement(this ReindexJobWrapper record)
        {
            EnsureArg.IsNotNull(record, nameof(record));

            var parametersResource = new Parameters();
            parametersResource.VersionId = record.ETag.VersionId;
            var job = record.JobRecord;

            parametersResource.Id = job.Id;
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();
            parametersResource.Add(JobRecordProperties.Id, new FhirString(job.Id));

            if (job.Error != null && job.Error.Count > 0)
            {
                var outputMessages = new List<Parameters.ParameterComponent>();
                foreach (var error in job.Error)
                {
                    outputMessages.Add(new Parameters.ParameterComponent() { Name = error.Code, Value = new FhirString(error.Diagnostics) });
                }

                parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Output, Part = outputMessages });
            }

            if (job.StartTime.HasValue)
            {
                parametersResource.Add(JobRecordProperties.StartTime, new FhirDateTime(job.StartTime.Value));
            }

            if (job.EndTime.HasValue)
            {
                parametersResource.Add(JobRecordProperties.EndTime, new FhirDateTime(job.EndTime.Value));
            }

            parametersResource.Add(JobRecordProperties.LastModified, new FhirDateTime(job.LastModified));

            decimal progress = 0;
            if (job.Count > 0 && job.Progress > 0)
            {
                progress = (decimal)job.Progress / job.Count * 100;
            }
            else if (job.ResourceCounts.Any())
            {
                progress = job.ResourceCounts.Values.Select(e => (((decimal)e.CurrentResourceSurrogateId - e.StartResourceSurrogateId) / ((decimal)e.EndResourceSurrogateId - e.StartResourceSurrogateId) * 100)).Sum() / job.ResourceCounts.Count;
            }
            else
            {
                progress = 0;
            }

            parametersResource.Add(JobRecordProperties.QueuedTime, new FhirDateTime(job.QueuedTime));
            parametersResource.Add(JobRecordProperties.TotalResourcesToReindex, new FhirDecimal(job.Count));
            parametersResource.Add(JobRecordProperties.ResourcesSuccessfullyReindexed, new FhirDecimal(job.Progress));
            parametersResource.Add(JobRecordProperties.Progress, new FhirDecimal(Math.Round(progress, 1)));
            parametersResource.Add(JobRecordProperties.Status, new FhirString(job.Status.ToString()));
            parametersResource.Add(JobRecordProperties.MaximumConcurrency, new FhirDecimal(job.MaximumConcurrency));

            if (!string.IsNullOrEmpty(job.ResourceList))
            {
                parametersResource.Add(JobRecordProperties.Resources, new FhirString(job.ResourceList));
            }

            if (!string.IsNullOrEmpty(job.SearchParamList))
            {
                parametersResource.Add(JobRecordProperties.SearchParams, new FhirString(job.SearchParamList));
            }

            if (!string.IsNullOrEmpty(job.TargetResourceTypeList))
            {
                parametersResource.Add(JobRecordProperties.TargetResourceTypes, new FhirString(job.TargetResourceTypeList));
            }

            if (!string.IsNullOrEmpty(job.TargetSearchParameterTypeList))
            {
                parametersResource.Add(JobRecordProperties.TargetSearchParameterTypes, new FhirString(job.TargetSearchParameterTypeList));
            }

            if (!string.IsNullOrEmpty(job.TargetDataStoreUsagePercentage.ToString()))
            {
                parametersResource.Add(JobRecordProperties.TargetDataStoreUsagePercentage, new FhirDecimal(job.TargetDataStoreUsagePercentage));
            }

            parametersResource.Add(JobRecordProperties.QueryDelayIntervalInMilliseconds, new FhirDecimal(job.QueryDelayIntervalInMilliseconds));
            parametersResource.Add(JobRecordProperties.MaximumNumberOfResourcesPerQuery, new FhirDecimal(job.MaximumNumberOfResourcesPerQuery));

            return parametersResource.ToResourceElement();
        }
    }
}
