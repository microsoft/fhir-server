// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of a bulk import operation.
    /// </summary>
    public class BulkImportResult : ResourceActionResult<string>
    {
        public BulkImportResult(HttpStatusCode statusCode)
            : base(null, statusCode)
        {
        }

        public BulkImportResult(string jobResult, HttpStatusCode statusCode)
            : base(jobResult, statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates an BulkImportResult with HttpStatusCode Accepted.
        /// </summary>
        public static BulkImportResult Accepted()
        {
            return new BulkImportResult(HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates an BulkImportResult with HttpStatusCode Ok.
        /// </summary>
        /// <param name="taskResult">The job payload that must be returned as part of the BulkImportResult.</param>
        public static BulkImportResult Ok(string taskResult)
        {
            return new BulkImportResult(taskResult, HttpStatusCode.OK);
        }
    }
}
