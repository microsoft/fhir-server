// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ReindexJobRecordExtensions
    {
        public static ResourceElement ToParametersResourceElement(this ReindexJobRecord record)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ReindexJobParameters.Id, Value = new FhirString(record.Id) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ReindexJobParameters.StartTime, Value = new FhirDateTime(record.StartTime.Value) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ReindexJobParameters.Progress, Value = new FhirDecimal(record.PercentComplete) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ReindexJobParameters.Status, Value = new FhirString(record.Status.ToString()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ReindexJobParameters.MaximumConcurrency, Value = new FhirDecimal(record.PercentComplete) });

            return parametersResource.ToResourceElement();
        }
    }
}
