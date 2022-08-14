// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Client.Authentication;
using NSubstitute;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestAuthenticationHttpMessageHandler : AuthenticationHttpMessageHandler
    {
        private readonly AuthenticationHeaderValue _headerValue;

        public TestAuthenticationHttpMessageHandler(AuthenticationHeaderValue headerValue)
            : base(Substitute.For<ICredentialProvider>())
        {
            _headerValue = headerValue;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.Authorization = _headerValue;

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
