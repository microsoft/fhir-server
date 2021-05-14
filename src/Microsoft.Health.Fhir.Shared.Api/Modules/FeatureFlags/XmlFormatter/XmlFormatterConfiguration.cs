// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter
{
    internal class XmlFormatterConfiguration : IProvideCapability
    {
        private readonly FeatureConfiguration _featureConfiguration;

        public XmlFormatterConfiguration(IOptions<FeatureConfiguration> featureConfiguration)
        {
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));

            _featureConfiguration = featureConfiguration.Value;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_featureConfiguration.SupportsXml)
            {
                builder.Apply(x =>
                {
                    x.Format.Add(KnownContentTypes.XmlContentType);
                    x.Format.Add("xml");
                });
            }
        }
    }
}
