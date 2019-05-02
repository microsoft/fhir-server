// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class TestHttpMessageHandler : DelegatingHandler
    {
        public TestHttpMessageHandler(HttpResponseMessage message)
        {
            Response = message;
        }

        public HttpResponseMessage Response { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await Task.FromResult(Response);
        }
    }
}
