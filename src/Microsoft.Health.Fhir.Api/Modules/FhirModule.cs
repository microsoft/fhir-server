// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of FHIR components
    /// </summary>
    public class FhirModule : IStartupModule
    {
        private readonly FeatureConfiguration _featureConfiguration;

        public FhirModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));
            _featureConfiguration = fhirServerConfiguration.Features;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            var jsonParser = new FhirJsonParser(DefaultParserSettings.Settings);
            var jsonSerializer = new FhirJsonSerializer();

            var xmlParser = new FhirXmlParser();
            var xmlSerializer = new FhirXmlSerializer();

            services.AddSingleton(jsonParser);
            services.AddSingleton(jsonSerializer);
            services.AddSingleton(xmlParser);
            services.AddSingleton(xmlSerializer);

            services.AddSingleton<IReadOnlyDictionary<ResourceFormat, Func<string, Resource>>>(x =>
            {
                return new Dictionary<ResourceFormat, Func<string, Resource>>
                {
                    { ResourceFormat.Json, str => jsonParser.Parse<Resource>(str) },
                    { ResourceFormat.Xml, str => xmlParser.Parse<Resource>(str) },
                };
            });

            services.AddSingleton<ResourceDeserializer>();

            services.Add<FormatterConfiguration>()
                .Singleton()
                .AsSelf()
                .AsService<IPostConfigureOptions<MvcOptions>>()
                .AsService<IProvideCapability>();

            services.AddSingleton<IContentTypeService, ContentTypeService>();
            services.AddSingleton<OperationOutcomeExceptionFilterAttribute>();
            services.AddSingleton<ValidateContentTypeFilterAttribute>();

            // HTML
            // If UI is supported, then add the formatter so that the
            // document can be output in HTML view.
            if (_featureConfiguration.SupportsUI)
            {
                services.Add<HtmlOutputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextOutputFormatter>();
            }

            services.Add<FhirJsonInputFormatter>()
                .Singleton()
                .AsSelf()
                .AsService<TextInputFormatter>();

            services.Add<FhirJsonOutputFormatter>()
                .Singleton()
                .AsSelf()
                .AsService<TextOutputFormatter>();

            if (_featureConfiguration.SupportsXml)
            {
                services.Add<FhirXmlInputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextInputFormatter>();

                services.Add<FhirXmlOutputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextOutputFormatter>();
            }

            services.Add<FhirRequestContextAccessor>()
                .Singleton()
                .AsSelf()
                .AsService<IFhirRequestContextAccessor>();

            services.AddSingleton<CorrelationIdProvider>(provider => () => Guid.NewGuid().ToString());

            // Add conformance provider for implementation metadata.
            services.AddSingleton<IConfiguredConformanceProvider, DefaultConformanceProvider>();

            services.Add<ConformanceProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IConformanceProvider>();

            services.Add<SystemConformanceProvider>()
                .Singleton()
                .AsSelf()
                .AsService<ISystemConformanceProvider>();

            services.Add<SecurityProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IProvideCapability>();

            services.TypesInSameAssemblyAs<IProvideCapability>()
                .AssignableTo<IProvideCapability>()
                .Transient()
                .AsService<IProvideCapability>();

            services.AddSingleton<INarrativeHtmlSanitizer, NarrativeHtmlSanitizer>();

            // Register a factory to resolve a scope that returns all components that provide capabilities
            services.AddFactory<IScoped<IEnumerable<IProvideCapability>>>();

            services.AddLazy();
            services.AddScoped();
        }
    }
}
