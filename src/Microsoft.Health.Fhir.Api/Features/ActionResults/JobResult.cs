// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class JobResult : ResourceActionResult<Parameters>
    {
        public JobResult(HttpStatusCode statusCode)
            : base(null, statusCode)
        {
        }

        public JobResult(Parameters jobResult, HttpStatusCode statusCode)
            : base(jobResult, statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        public static JobResult FromResults(IEnumerable<Tuple<string, Base>> results, IList<OperationOutcomeIssue> issues, HttpStatusCode statusCode, string resultsTitle = "Results")
        {
            var resource = new Parameters();

            if (issues?.Count > 0)
            {
                var operationOutcome = new OperationOutcome();
                foreach (var issue in issues)
                {
                    operationOutcome.Issue.Add(issue.ToPoco());
                }

                resource.Add("Issues", operationOutcome);
            }

            if (results?.GetEnumerator().MoveNext() == true)
            {
                resource.Add(resultsTitle, results);
            }

            return new JobResult(resource, statusCode);
        }

        /// <summary>
        /// Creates a JobResult with HttpStatusCode Accepted.
        /// </summary>
        public static JobResult Accepted()
        {
            return new JobResult(HttpStatusCode.Accepted);
        }
    }
}
