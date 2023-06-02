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
            decimal rounded = 0;
            if (job.Count > 0 && job.Progress > 0)
            {
                progress = (decimal)job.Progress / job.Count * 100;
                rounded = Math.Round(progress, 1);
            }
            else
            {
                progress = 0;
            }

            if (rounded == 100.0M && job.Count != job.Progress)
            {
                rounded = 99.9M;
            }

            parametersResource.Add(JobRecordProperties.QueuedTime, new FhirDateTime(job.QueuedTime));
            parametersResource.Add(JobRecordProperties.TotalResourcesToReindex, new FhirDecimal(job.Count));
            parametersResource.Add(JobRecordProperties.ResourcesSuccessfullyReindexed, new FhirDecimal(job.Progress));
            parametersResource.Add(JobRecordProperties.Progress, new FhirDecimal(rounded));
            parametersResource.Add(JobRecordProperties.Status, new FhirString(job.Status.ToString()));
            parametersResource.Add(JobRecordProperties.MaximumConcurrency, new FhirDecimal(job.MaximumConcurrency));

            if (!string.IsNullOrEmpty(job.ResourceList))
            {
                parametersResource.Add(JobRecordProperties.Resources, new FhirString(job.ResourceList));
            }

            var resourcesWithCounts = job.ResourceCounts.Where(e => e.Value.Count > 0).ToList();
            if (resourcesWithCounts.Any())
            {
                string msgLabelSuffix = string.Empty;
                var outputMessages = new StringBuilder();
                bool hasValue = false;

                foreach (KeyValuePair<string, Features.Search.SearchResultReindex> kvp in resourcesWithCounts)
                {
                    // because this is a newer field that we want to display and to be backwards compatible, we'll only display this if the CountReindexed > 0
                    hasValue = kvp.Value.CountReindexed > 0;

                    if (hasValue)
                    {
                        if (string.IsNullOrWhiteSpace(msgLabelSuffix))
                        {
                            msgLabelSuffix = $" ({nameof(kvp.Value.CountReindexed)} of {nameof(kvp.Value.Count)})";
                        }

                        outputMessages.AppendLine($"{kvp.Key}: {kvp.Value.CountReindexed.ToString("N0")} of {kvp.Value.Count.ToString("N0")}");
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(msgLabelSuffix))
                        {
                            msgLabelSuffix = " (resource count)";
                        }

                        outputMessages.AppendLine($"{kvp.Key}: {kvp.Value.Count.ToString("N0")}");
                    }

                    hasValue = false;
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
