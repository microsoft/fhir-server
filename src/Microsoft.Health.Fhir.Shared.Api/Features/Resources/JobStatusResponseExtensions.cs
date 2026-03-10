// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    /// <summary>
    /// Extension methods for job status responses.
    /// </summary>
    public static class JobStatusResponseExtensions
    {
        /// <summary>
        /// Converts a list of JobStatusInfo to a FHIR Parameters resource.
        /// </summary>
        /// <param name="jobs">The list of job status information.</param>
        /// <returns>A FHIR Parameters resource as a ResourceElement</returns>
        public static ResourceElement ToJobStatusResult(this IReadOnlyList<JobStatusInfo> jobs)
        {
            var parameters = new Parameters();

            foreach (var job in jobs)
            {
                var part = new Parameters.ParameterComponent
                {
                    Name = job.JobType + " " + job.GroupId,
                };

                part.Part.Add(new Parameters.ParameterComponent
                {
                    Name = "id",
                    Value = new Integer64(job.GroupId),
                });

                part.Part.Add(new Parameters.ParameterComponent
                {
                    Name = "type",
                    Value = new FhirString(job.JobType),
                });

                part.Part.Add(new Parameters.ParameterComponent
                {
                    Name = "uri",
                    Value = new FhirUri(job.ContentLocation),
                });

                part.Part.Add(new Parameters.ParameterComponent
                {
                    Name = "status",
                    Value = new FhirString(job.Status.ToString()),
                });

                part.Part.Add(new Parameters.ParameterComponent
                {
                    Name = "createTime",
                    Value = new FhirDateTime(job.CreateDate),
                });

                if (job.EndDate != null)
                {
                    part.Part.Add(new Parameters.ParameterComponent
                    {
                        Name = "endTime",
                        Value = new FhirDateTime((System.DateTimeOffset)job.EndDate),
                    });
                }

                parameters.Parameter.Add(part);
            }

            return parameters.ToResourceElement();
        }
    }
}
