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
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetSmartConfigurationHandler : IRequestHandler<GetSmartConfigurationRequest, GetSmartConfigurationResponse>
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IModelInfoProvider _modelInfoProider;

        public GetSmartConfigurationHandler(SecurityConfiguration securityConfiguration, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _securityConfiguration = securityConfiguration;
            _modelInfoProider = modelInfoProvider;
        }

        public Task<GetSmartConfigurationResponse> Handle(GetSmartConfigurationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handle(request));
        }

        protected GetSmartConfigurationResponse Handle(GetSmartConfigurationRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (_securityConfiguration.Authorization.Enabled)
            {
                string baseEndpoint = _securityConfiguration.Authentication.Authority;

                try
                {
                    Uri authorizationEndpoint = new Uri(string.Join(baseEndpoint, "/authorize"));
                    Uri tokenEndpoint = new Uri(string.Join(baseEndpoint, "/token"));
                    ICollection<string> capabilities = new List<string>
                    {
                        "launch-standalone",
                        "client-public",
                        "client-confidential-symmetric",
                        "sso-openid-connect",
                        "context-standalone-patient",
                        "permission-offline",
                        "permission-patient",
                    };

                    return new GetSmartConfigurationResponse(authorizationEndpoint, tokenEndpoint, capabilities);
                }
                catch (Exception e) when (e is ArgumentNullException || e is UriFormatException)
                {
                    throw new OperationFailedException("Security configuration base endpoint is not a valid Uri", HttpStatusCode.BadRequest);
                }
            }

            throw new OperationFailedException("Security configuration authorization is not enabled.", HttpStatusCode.BadRequest);
        }
    }
}
