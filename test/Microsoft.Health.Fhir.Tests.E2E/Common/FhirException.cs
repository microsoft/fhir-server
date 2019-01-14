// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class FhirException : Exception
    {
        private readonly FhirResponse<OperationOutcome> _response;

        public FhirException(FhirResponse<OperationOutcome> response)
        {
            _response = response;
        }

        public HttpStatusCode StatusCode => _response.StatusCode;

        public HttpResponseHeaders Headers => _response.Headers;

        public FhirResponse<OperationOutcome> Response => _response;

        public HttpContent Content => _response.Content;

        public OperationOutcome OperationOutcome => _response.Resource;

        public override string Message
            => $"{StatusCode}: {OperationOutcome?.Issue?.FirstOrDefault().Diagnostics}";
    }
}
