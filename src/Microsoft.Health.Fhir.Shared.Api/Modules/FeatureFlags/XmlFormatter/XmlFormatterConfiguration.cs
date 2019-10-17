// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter
{
    internal class XmlFormatterConfiguration : IPostConfigureOptions<MvcOptions>, IProvideCapability
    {
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly IConfiguredConformanceProvider _configuredConformanceProvider;

        public XmlFormatterConfiguration(
            IOptions<FeatureConfiguration> featureConfiguration,
            IConfiguredConformanceProvider configuredConformanceProvider,
            IEnumerable<TextInputFormatter> inputFormatters,
            IEnumerable<TextOutputFormatter> outputFormatters)
        {
            EnsureArg.IsNotNull(featureConfiguration, nameof(featureConfiguration));
            EnsureArg.IsNotNull(featureConfiguration.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(inputFormatters, nameof(inputFormatters));
            EnsureArg.IsNotNull(outputFormatters, nameof(outputFormatters));

            _featureConfiguration = featureConfiguration.Value;
            _configuredConformanceProvider = configuredConformanceProvider;
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            if (_featureConfiguration.SupportsXml)
            {
                _configuredConformanceProvider
                    .ConfigureOptionalCapabilities(statement => statement.Format.Add(KnownContentTypes.XmlContentType));
            }
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_featureConfiguration.SupportsXml)
            {
                builder.Update(x => x.Format.Add(KnownContentTypes.XmlContentType));
            }
        }
    }
}
