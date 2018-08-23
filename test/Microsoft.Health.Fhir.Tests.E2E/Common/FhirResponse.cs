// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class FhirResponse
    {
        private readonly HttpResponseMessage _response;

        public FhirResponse(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpStatusCode StatusCode => _response.StatusCode;

        public HttpResponseHeaders Headers => _response.Headers;

        public HttpContent Content => _response.Content;
    }
}
