// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

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
                string json = JsonConvert.SerializeObject(new SmartConfiguration(baseEndpoint));
                ISourceNode jsonStatement = FhirJsonNode.Parse(json);
                ResourceElement resourceElement = jsonStatement
                    .ToTypedElement(_modelInfoProider.StructureDefinitionSummaryProvider)
                    .ToResourceElement();

                return new GetSmartConfigurationResponse(resourceElement);
            }

            throw new OperationFailedException("Security configuration authorization is not enabled.", HttpStatusCode.Unauthorized);
        }
    }
}
