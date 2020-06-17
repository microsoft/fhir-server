// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class TooManyRequestsActionResult : ResourceActionResult<OperationOutcomeIssue>
    {
        public static readonly TooManyRequestsActionResult TooManyRequests = new TooManyRequestsActionResult();

        public TooManyRequestsActionResult()
            : base(
                  new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Throttled,
                        Resources.TooManyConcurrentRequests),
                  HttpStatusCode.TooManyRequests)
        {
        }
    }
}
