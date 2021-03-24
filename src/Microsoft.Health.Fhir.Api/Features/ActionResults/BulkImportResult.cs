// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of an export operation.
    /// </summary>
    public class BulkImportResult : ResourceActionResult<BulkImportJobResult>
    {
        public BulkImportResult(HttpStatusCode statusCode)
            : base(null, statusCode)
        {
        }

        public BulkImportResult(BulkImportJobResult jobResult, HttpStatusCode statusCode)
            : base(jobResult, statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates an ExportResult with HttpStatusCode Accepted.
        /// </summary>
        public static BulkImportResult Accepted()
        {
            return new BulkImportResult(HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates an ExportResult with HttpStatusCode Ok.
        /// </summary>
        /// <param name="jobResult">The job payload that must be returned as part of the ExportResult.</param>
        public static BulkImportResult Ok(BulkImportJobResult jobResult)
        {
            return new BulkImportResult(jobResult, HttpStatusCode.OK);
        }
    }
}
