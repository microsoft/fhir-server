// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// This action result is specifically used when we want to return an error
    /// to the client with the appropriate OperationOutcome.
    /// </summary>
    public class OperationOutcomeResult : BaseActionResult<OperationOutcome>
    {
        public OperationOutcomeResult(OperationOutcome outcome, HttpStatusCode statusCode)
            : base(outcome, statusCode)
        {
            EnsureArg.IsNotNull(outcome, nameof(outcome));
        }
    }
}
