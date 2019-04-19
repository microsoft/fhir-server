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
        private HttpResponseMessage _response;

        public TestHttpMessageHandler(HttpResponseMessage message)
        {
            _response = message;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await Task.FromResult(_response);
        }
    }
}
