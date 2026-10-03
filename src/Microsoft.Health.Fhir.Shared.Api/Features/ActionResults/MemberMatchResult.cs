// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public sealed class MemberMatchResult : ResourceActionResult<Parameters>
    {
        private MemberMatchResult(Parameters parameters, HttpStatusCode statusCode, ILogger logger)
            : base(parameters, statusCode, logger)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));
        }

        /// <summary>
        /// Creates an <see cref="MemberMatchResult"/> with <see cref="HttpStatusCode.OK"/>
        /// </summary>
        /// <param name="parameters">Parameters object containing Patient with identifier.</param>
        /// <param name="logger">The logger.</param>
        public static MemberMatchResult Ok(Parameters parameters, ILogger logger)
        {
            return new MemberMatchResult(parameters, HttpStatusCode.OK, logger);
        }
    }
}
