// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Get;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetCapabilitiesHandler :
        IRequestHandler<GetCapabilitiesRequest, GetCapabilitiesResponse>,
        IRequestHandler<GetSystemCapabilitiesRequest, GetSystemCapabilitiesResponse>
    {
        private readonly IConformanceProvider _provider;
        private readonly ISystemConformanceProvider _systemConformanceProvider;
        private readonly FhirJsonParser _parser;
        private readonly IUrlResolver _urlResolver;

        public GetCapabilitiesHandler(IConformanceProvider provider, ISystemConformanceProvider systemConformanceProvider, FhirJsonParser parser, IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(provider, nameof(provider));
            EnsureArg.IsNotNull(systemConformanceProvider, nameof(systemConformanceProvider));
            EnsureArg.IsNotNull(parser, nameof(parser));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _provider = provider;
            _systemConformanceProvider = systemConformanceProvider;
            _parser = parser;
            _urlResolver = urlResolver;
        }

        public async Task<GetCapabilitiesResponse> Handle(GetCapabilitiesRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var response = await _provider.GetCapabilityStatementAsync(cancellationToken);

            return new GetCapabilitiesResponse(response);
        }

        public async Task<GetSystemCapabilitiesResponse> Handle(GetSystemCapabilitiesRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var systemListedCapabilities = await _systemConformanceProvider.GetSystemListedCapabilitiesStatementAsync(cancellationToken);

            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{GetType().Namespace}.AllCapabilities.json"))
            using (var reader = new StreamReader(resourceStream))
            {
                var allCapabilities = _parser.Parse<CapabilityStatement>(await reader.ReadToEndAsync());

                var mergedSystemCapabilities = systemListedCapabilities.Intersect(allCapabilities, strictConfig: false);
                mergedSystemCapabilities.UrlElement = new FhirUri(_urlResolver.ResolveMetadataUrl(true));

                return new GetSystemCapabilitiesResponse(mergedSystemCapabilities.ToResourceElement());
            }
        }
    }
}
