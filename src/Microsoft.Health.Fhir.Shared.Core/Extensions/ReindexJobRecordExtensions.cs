// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
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

            parametersResource.Add(JobRecordProperties.QueuedTime, new FhirDateTime(job.QueuedTime));
            parametersResource.Add(JobRecordProperties.TotalResourcesToReindex, new FhirDecimal(job.Count));
            parametersResource.Add(JobRecordProperties.ResourcesSuccessfullyReindexed, new FhirDecimal(job.Progress));
            parametersResource.Add(JobRecordProperties.Progress, new FhirDecimal(job.PercentComplete));
            parametersResource.Add(JobRecordProperties.Status, new FhirString(job.Status.ToString()));
            parametersResource.Add(JobRecordProperties.MaximumConcurrency, new FhirDecimal(job.MaximumConcurrency));
            parametersResource.Add(JobRecordProperties.Resources, new FhirString(job.ResourceList));
            parametersResource.Add(JobRecordProperties.SearchParams, new FhirString(job.SearchParamList));
            parametersResource.Add(JobRecordProperties.TargetResourceTypes, new FhirString(job.TargetResourceTypeList));

            return parametersResource.ToResourceElement();
        }
    }
}
