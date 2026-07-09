// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter
{
    internal class XmlFormatterConfiguration : IProvideCapability
    {
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly FhirSdkMode _sdkMode;

        public XmlFormatterConfiguration(IOptions<FhirServerConfiguration> fhirServerConfiguration, IOptions<FeatureConfiguration> featureConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration?.Value, nameof(fhirServerConfiguration));
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));

            _sdkMode = fhirServerConfiguration.Value.Sdk.Mode;
            _featureConfiguration = featureConfiguration.Value;
        }

        public Task BuildAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
        {
            if (_featureConfiguration.SupportsXml && _sdkMode != FhirSdkMode.Ignixa)
            {
                builder.Apply(x =>
                {
                    x.Format.Add(KnownContentTypes.XmlContentType);
                    x.Format.Add("xml");
                });
            }

            return Task.CompletedTask;
        }
    }
}
