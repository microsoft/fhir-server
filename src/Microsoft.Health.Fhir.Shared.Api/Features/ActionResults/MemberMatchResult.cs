// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public sealed class MemberMatchResult : ResourceActionResult<Parameters>
    {
        private MemberMatchResult(Parameters parameters, HttpStatusCode statusCode)
            : base(parameters, statusCode)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));
        }

        /// <summary>
        /// Creates an <see cref="MemberMatchResult"/> with <see cref="HttpStatusCode.OK"/>
        /// </summary>
        /// <param name="parameters">Parameters object containing Patient with identifier.</param>
        public static MemberMatchResult Ok(Parameters parameters)
        {
            return new MemberMatchResult(parameters, HttpStatusCode.OK);
        }
    }
}
