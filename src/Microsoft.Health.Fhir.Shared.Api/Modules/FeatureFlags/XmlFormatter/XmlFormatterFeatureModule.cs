// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter
{
    public class XmlFormatterFeatureModule : IStartupModule
    {
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly FhirSdkMode _sdkMode;

        public XmlFormatterFeatureModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));
            _featureConfiguration = fhirServerConfiguration.Features;
            _sdkMode = fhirServerConfiguration.Sdk.Mode;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            if (_featureConfiguration.SupportsXml && _sdkMode != FhirSdkMode.Ignixa)
            {
                // Text Formatters will be registered with MVC in FormatterConfiguration.cs

                services.Add<FhirXmlInputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextInputFormatter>();

                services.Add<FhirXmlOutputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextOutputFormatter>();

                services.Add<NonFhirResourceXmlOutputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextOutputFormatter>();
            }
        }
    }
}
