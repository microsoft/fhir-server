// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

            var startTime = job.StartTime ?? DateTimeOffset.MinValue;
            var endTime = job.StartTime ?? DateTimeOffset.MaxValue;
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Id, Value = new FhirString(job.Id) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.StartTime, Value = new FhirDateTime(startTime) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.EndTime, Value = new FhirDateTime(endTime) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Progress, Value = new FhirDecimal(job.PercentComplete) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Status, Value = new FhirString(job.Status.ToString()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.MaximumConcurrency, Value = new FhirDecimal(job.MaximumConcurrency) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.Scope, Value = new FhirString(job.Scope) });

            return parametersResource.ToResourceElement();
        }
    }
}
