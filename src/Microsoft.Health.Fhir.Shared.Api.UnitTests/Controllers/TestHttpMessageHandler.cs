// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class TestHttpMessageHandler : DelegatingHandler
    {
        public TestHttpMessageHandler(HttpResponseMessage message, Exception exception = null)
        {
            Response = message;
            Exception = exception;
        }

        public HttpResponseMessage Response { get; set; }

        public Exception Exception { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Exception != null)
            {
                throw Exception;
            }

            return await Task.FromResult(Response);
        }
    }
}
