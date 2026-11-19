// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of a bulk import operation.
    /// </summary>
    public class ImportResult : ResourceActionResult<ImportJobResult>
    {
        public ImportResult(HttpStatusCode statusCode, ILogger logger)
            : base(null, statusCode, logger)
        {
        }

        public ImportResult(ImportJobResult jobResult, HttpStatusCode statusCode, ILogger logger)
            : base(jobResult, statusCode, logger)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates an ImportResult with HttpStatusCode Accepted.
        /// </summary>
        public static ImportResult Accepted(ILogger logger)
        {
            return new ImportResult(HttpStatusCode.Accepted, logger);
        }

        /// <summary>
        /// Creates an ImportResult with HttpStatusCode Accepted.
        /// </summary>
        /// <param name="taskResult">The job payload that must be returned as part of the ImportResult.</param>
        /// <param name="logger">The logger.</param>
        public static ImportResult Accepted(ImportJobResult taskResult, ILogger logger)
        {
            EnsureArg.IsNotNull(taskResult, nameof(taskResult));

            return new ImportResult(taskResult, HttpStatusCode.Accepted, logger);
        }

        /// <summary>
        /// Creates an ImportResult with HttpStatusCode Ok.
        /// </summary>
        /// <param name="taskResult">The job payload that must be returned as part of the ImportResult.</param>
        /// <param name="logger">The logger.</param>
        public static ImportResult Ok(ImportJobResult taskResult, ILogger logger)
        {
            EnsureArg.IsNotNull(taskResult, nameof(taskResult));

            return new ImportResult(taskResult, HttpStatusCode.OK, logger);
        }
    }
}
