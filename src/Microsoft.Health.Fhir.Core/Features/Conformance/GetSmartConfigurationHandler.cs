// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetSmartConfigurationHandler : IRequestHandler<GetSmartConfigurationRequest, GetSmartConfigurationResponse>
    {
        private readonly SecurityConfiguration _securityConfiguration;

        public GetSmartConfigurationHandler(IOptions<SecurityConfiguration> securityConfigurationOptions)
        {
            EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));

            _securityConfiguration = securityConfigurationOptions.Value;
        }

        public Task<GetSmartConfigurationResponse> Handle(GetSmartConfigurationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handle(request));
        }

        protected GetSmartConfigurationResponse Handle(GetSmartConfigurationRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (_securityConfiguration.Authorization.Enabled || _securityConfiguration.Authorization.EnableSmartWithoutAuth)
            {
                string baseEndpoint = _securityConfiguration.Authentication.Authority;

                try
                {
                    Uri authorizationEndpoint = new Uri(baseEndpoint + "/authorize");
                    Uri tokenEndpoint = new Uri(baseEndpoint + "/token");
                    ICollection<string> capabilities = new List<string>
                    {
                        "sso-openid-connect",
                        "permission-offline",
                        "permission-patient",
                        "permission-user",
                    };

                    return new GetSmartConfigurationResponse(authorizationEndpoint, tokenEndpoint, capabilities);
                }
                catch (Exception e) when (e is ArgumentNullException || e is UriFormatException)
                {
                    throw new OperationFailedException(
                        string.Format(Core.Resources.InvalidSecurityConfigurationBaseEndpoint, nameof(SecurityConfiguration.Authentication.Authority)),
                        HttpStatusCode.BadRequest);
                }
            }

            throw new OperationFailedException(
                Core.Resources.SecurityConfigurationAuthorizationNotEnabled,
                HttpStatusCode.BadRequest);
        }
    }
}
