// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Reset.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of an reset operation.
    /// </summary>
    public class ResetResult : ResourceActionResult<ResetTaskResult>
    {
        public ResetResult(HttpStatusCode statusCode)
            : base(null, statusCode)
        {
        }

        public ResetResult(ResetTaskResult jobResult, HttpStatusCode statusCode)
            : base(jobResult, statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates an ResetResult with HttpStatusCode Accepted.
        /// </summary>
        public static ResetResult Accepted()
        {
            return new ResetResult(HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates an ResetResult with HttpStatusCode Ok.
        /// </summary>
        /// <param name="taskResult">The job payload that must be returned as part of the ResetResult.</param>
        public static ResetResult Ok(ResetTaskResult taskResult)
        {
            return new ResetResult(taskResult, HttpStatusCode.OK);
        }
    }
}
