// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Net;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "This is for Factory pattern usage")]
    public class JobResult<TResult> : ResourceActionResult<TResult>
        where TResult : class
    {
        public JobResult(HttpStatusCode statusCode)
            : base(null, statusCode)
        {
        }

        public JobResult(TResult jobResult, HttpStatusCode statusCode)
            : base(jobResult, statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates a Result with HttpStatusCode Accepted.
        /// </summary>
        public static JobResult<TResult> Accepted()
        {
            return new JobResult<TResult>(HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates a Result with HttpStatusCode Ok.
        /// </summary>
        /// <param name="jobResult">The job payload that must be returned as part of the Result.</param>
        public static JobResult<TResult> Ok(TResult jobResult)
        {
            return new JobResult<TResult>(jobResult, HttpStatusCode.OK);
        }
    }
}
