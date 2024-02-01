// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetSmartConfigurationHandler : IRequestHandler<GetSmartConfigurationRequest, GetSmartConfigurationResponse>
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IHttpClientFactory _httpClientFactory;

        public GetSmartConfigurationHandler(IOptions<SecurityConfiguration> securityConfigurationOptions, IHttpClientFactory httpClientFactory)
        {
            _securityConfiguration = EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));
            _httpClientFactory = EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
        }

        public async Task<GetSmartConfigurationResponse> Handle(GetSmartConfigurationRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (!_securityConfiguration.Authorization.Enabled && !_securityConfiguration.Authorization.EnableSmartWithoutAuth)
            {
                throw new OperationFailedException(
                Core.Resources.SecurityConfigurationAuthorizationNotEnabled,
                HttpStatusCode.BadRequest);
            }

            Uri openidConfigurationUri = GetOpenIdConfigurationUri();

            if (openidConfigurationUri != null)
            {
                using HttpClient client = _httpClientFactory.CreateClient();
                using var configurationRequest = new HttpRequestMessage(HttpMethod.Get, openidConfigurationUri);
                HttpResponseMessage response = await client.SendAsync(configurationRequest, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string configuration = await response.Content.ReadAsStringAsync(cancellationToken);
                    return JsonConvert.DeserializeObject<GetSmartConfigurationResponse>(configuration);
                }
            }

            throw new OperationFailedException(
                string.Format(Core.Resources.InvalidSecurityConfigurationBaseEndpoint, nameof(SecurityConfiguration.Authentication.Authority)),
                HttpStatusCode.BadRequest);
        }

        private Uri GetOpenIdConfigurationUri()
        {
            // Prefer the SmartAuthentication authority, but default to Authentication authority.
            string authority = _securityConfiguration?.SmartAuthentication?.Authority ?? _securityConfiguration?.Authentication?.Authority;

            if (authority != null)
            {
                try
                {
                    Uri authorityUri = new Uri(authority);
                    return new Uri(authorityUri, ".well-known/openid-configuration");
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
