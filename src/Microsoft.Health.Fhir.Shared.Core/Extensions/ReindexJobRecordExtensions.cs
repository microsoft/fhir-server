// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            decimal rounded = 0;
            long totalResourceCount = job.ResourceCounts?.Sum(entry => entry.Value.Count) ?? 0;

            if (totalResourceCount > 0 && job.Progress > 0)
            {
                progress = (decimal)job.Progress / totalResourceCount * 100;
                rounded = Math.Round(progress, 1);
            }
            else
            {
                progress = 0;
            }

            if (rounded == 100.0M && totalResourceCount != job.Progress)
            {
                rounded = 99.9M;
            }

            parametersResource.Add(JobRecordProperties.QueuedTime, new FhirDateTime(job.QueuedTime));
            parametersResource.Add(JobRecordProperties.TotalResourcesToReindex, new FhirDecimal(totalResourceCount));
            parametersResource.Add(JobRecordProperties.ResourcesSuccessfullyReindexed, new FhirDecimal(job.Progress));
            parametersResource.Add(JobRecordProperties.Progress, new FhirDecimal(rounded));
            parametersResource.Add(JobRecordProperties.Status, new FhirString(job.Status.ToString()));

            if (!string.IsNullOrEmpty(job.ResourceList))
            {
                parametersResource.Add(JobRecordProperties.Resources, new FhirString(job.ResourceList));
            }

            var resourcesWithCounts = job.ResourceCounts.Where(e => e.Value.Count > 0).ToList();
            if (resourcesWithCounts.Any())
            {
                string msgLabelSuffix = string.Empty;
                var outputMessages = new StringBuilder();

                foreach (KeyValuePair<string, Features.Search.SearchResultReindex> kvp in resourcesWithCounts)
                {
                    if (string.IsNullOrWhiteSpace(msgLabelSuffix))
                    {
                        msgLabelSuffix = " (resource count)";
                    }

                    outputMessages.AppendLine($"{kvp.Key}: {kvp.Value.Count.ToString("N0")}");
                }

                if (outputMessages.Length > 0)
                {
                    parametersResource.Add($"{JobRecordProperties.ResourceReindexProgressByResource}{msgLabelSuffix}", new FhirString(outputMessages.ToString()));
                }
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

            if (!string.IsNullOrEmpty(job.FailureDetails?.FailureReason))
            {
                parametersResource.Add(JobRecordProperties.FailureDetails, new FhirString(job.FailureDetails.FailureReason));
            }

            parametersResource.Add(JobRecordProperties.MaximumNumberOfResourcesPerQuery, new FhirDecimal(job.MaximumNumberOfResourcesPerQuery));

            parametersResource.Add(JobRecordProperties.MaximumNumberOfResourcesPerWrite, new FhirDecimal(job.MaximumNumberOfResourcesPerWrite));

            return parametersResource.ToResourceElement();
        }
    }
}
