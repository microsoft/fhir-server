// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    internal class AuthorizationMessageHandler : HttpClientHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue Authorization { get; set; }

        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Authorization != null)
            {
                request.Headers.Authorization = Authorization;
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
