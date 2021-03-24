// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Features.Operations.Reset.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Reset
{
    public class GetResetResponse
    {
        public GetResetResponse(HttpStatusCode statusCode)
            : this(statusCode, jobResult: null)
        {
        }

        public GetResetResponse(HttpStatusCode statusCode, ResetJobResult jobResult)
        {
            StatusCode = statusCode;
            JobResult = jobResult;
        }

        public HttpStatusCode StatusCode { get; }

        public ResetJobResult JobResult { get; }
    }
}
