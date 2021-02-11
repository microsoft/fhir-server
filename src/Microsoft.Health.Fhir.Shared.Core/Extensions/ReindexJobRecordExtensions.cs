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
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Id, Value = new FhirString(job.Id) });

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
                parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.StartTime, Value = new FhirDateTime(job.StartTime.Value) });
            }

            if (job.EndTime.HasValue)
            {
                parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.EndTime, Value = new FhirDateTime(job.EndTime.Value) });
            }

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.QueuedTime, Value = new FhirDateTime(job.QueuedTime) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.TotalResourcesToReindex, Value = new FhirDecimal(job.Count) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.ResourcesSuccessfullyReindexed, Value = new FhirDecimal(job.Progress) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Progress, Value = new FhirDecimal(job.PercentComplete) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Status, Value = new FhirString(job.Status.ToString()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.MaximumConcurrency, Value = new FhirDecimal(job.MaximumConcurrency) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Resources, Value = new FhirString(job.ResourceList) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.SearchParams, Value = new FhirString(job.SearchParamList) });

            return parametersResource.ToResourceElement();
        }
    }
}
