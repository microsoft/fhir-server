// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of an export operation.
    /// </summary>
    public class ExportResult : ResourceActionResult<ExportJobResult>
    {
        public ExportResult(HttpStatusCode statusCode, ILogger logger)
            : base(null, statusCode, logger)
        {
        }

        public ExportResult(ExportJobResult jobResult, HttpStatusCode statusCode, ILogger logger)
            : base(jobResult, statusCode, logger)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));
        }

        /// <summary>
        /// Creates an ExportResult with HttpStatusCode Accepted.
        /// </summary>
        public static ExportResult Accepted(ILogger logger)
        {
            return new ExportResult(HttpStatusCode.Accepted, logger);
        }

        /// <summary>
        /// Creates an ExportResult with HttpStatusCode Ok.
        /// </summary>
        /// <param name="jobResult">The job payload that must be returned as part of the ExportResult.</param>
        /// <param name="logger">The logger.</param>
        public static ExportResult Ok(ExportJobResult jobResult, ILogger logger)
        {
            return new ExportResult(jobResult, HttpStatusCode.OK, logger);
        }
    }
}
