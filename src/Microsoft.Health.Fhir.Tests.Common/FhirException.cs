// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class FhirException : Exception
    {
        private readonly FhirResponse<ResourceElement> _response;

        public FhirException(FhirResponse<ResourceElement> response)
        {
            _response = response;
        }

        public HttpStatusCode StatusCode => _response.StatusCode;

        public HttpResponseHeaders Headers => _response.Headers;

        public FhirResponse<ResourceElement> Response => _response;

        public HttpContent Content => _response.Content;

        public ResourceElement OperationOutcome => _response.Resource;

        public override string Message
            => $"{StatusCode}: {OperationOutcome.Scalar<string>("Resource.issue.first().diagnostics")}";
    }
}
