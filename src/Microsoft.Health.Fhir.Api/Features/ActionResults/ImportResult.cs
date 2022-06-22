// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of a bulk import operation.
    /// </summary>
    public class ImportResult : ResourceActionResult<ImportJobResult>
    {
        public ImportResult(HttpStatusCode statusCode)
            : base(null, statusCode)
        {
        }

        public ImportResult(ImportJobResult jobResult, HttpStatusCode statusCode)
            : base(jobResult, statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates an ImportResult with HttpStatusCode Accepted.
        /// </summary>
        public static ImportResult Accepted()
        {
            return new ImportResult(HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates an ImportResult with HttpStatusCode Accepted.
        /// </summary>
        /// <param name="taskResult">The job payload that must be returned as part of the ImportResult.</param>
        public static ImportResult Accepted(ImportJobResult taskResult)
        {
            EnsureArg.IsNotNull(taskResult, nameof(taskResult));

            return new ImportResult(taskResult, HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates an ImportResult with HttpStatusCode Ok.
        /// </summary>
        /// <param name="taskResult">The job payload that must be returned as part of the ImportResult.</param>
        public static ImportResult Ok(ImportJobResult taskResult)
        {
            EnsureArg.IsNotNull(taskResult, nameof(taskResult));

            return new ImportResult(taskResult, HttpStatusCode.OK);
        }
    }
}
