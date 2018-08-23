// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FormatterConfiguration : IPostConfigureOptions<MvcOptions>, IProvideCapability
    {
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly IConfiguredConformanceProvider _configuredConformanceProvider;
        private readonly FhirJsonInputFormatter _fhirJsonInputFormatter;
        private readonly FhirXmlInputFormatter _fhirXmlInputFormatter;
        private readonly HtmlOutputFormatter _htmlOutputFormatter;
        private readonly FhirJsonOutputFormatter _fhirJsonOutputFormatter;
        private readonly FhirXmlOutputFormatter _fhirXmlOutputFormatter;

        public FormatterConfiguration(
            IOptions<FeatureConfiguration> featureConfiguration,
            IConfiguredConformanceProvider configuredConformanceProvider,
            FhirJsonInputFormatter fhirJsonInputFormatter,
            FhirXmlInputFormatter fhirXmlInputFormatter,
            HtmlOutputFormatter htmlOutputFormatter,
            FhirJsonOutputFormatter fhirJsonOutputFormatter,
            FhirXmlOutputFormatter fhirXmlOutputFormatter)
        {
            EnsureArg.IsNotNull(featureConfiguration, nameof(featureConfiguration));
            EnsureArg.IsNotNull(featureConfiguration.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(fhirJsonInputFormatter, nameof(fhirJsonInputFormatter));
            EnsureArg.IsNotNull(fhirXmlInputFormatter, nameof(fhirXmlInputFormatter));
            EnsureArg.IsNotNull(htmlOutputFormatter, nameof(htmlOutputFormatter));
            EnsureArg.IsNotNull(fhirJsonOutputFormatter, nameof(fhirJsonOutputFormatter));
            EnsureArg.IsNotNull(fhirXmlOutputFormatter, nameof(fhirXmlOutputFormatter));

            _featureConfiguration = featureConfiguration.Value;
            _configuredConformanceProvider = configuredConformanceProvider;
            _fhirJsonInputFormatter = fhirJsonInputFormatter;
            _fhirXmlInputFormatter = fhirXmlInputFormatter;
            _htmlOutputFormatter = htmlOutputFormatter;
            _fhirJsonOutputFormatter = fhirJsonOutputFormatter;
            _fhirXmlOutputFormatter = fhirXmlOutputFormatter;
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            // JSON
            options.InputFormatters.Insert(0, _fhirJsonInputFormatter);
            options.OutputFormatters.Insert(0, _fhirJsonOutputFormatter);

            // XML
            if (_featureConfiguration.SupportsXml)
            {
                options.InputFormatters.Insert(1, _fhirXmlInputFormatter);
                options.OutputFormatters.Insert(1, _fhirXmlOutputFormatter);

                // TODO: This feature flag should be removed when we support custom capability statements
                _configuredConformanceProvider.ConfigureOptionalCapabilities(statement => statement.Format = statement.Format.Concat(new[] { ContentType.XML_CONTENT_HEADER }));
            }

            // HTML
            // If UI is supported, then add the formatter so that the
            // document can be output in HTML view.
            if (_featureConfiguration.SupportsUI)
            {
                options.OutputFormatters.Insert(0, _htmlOutputFormatter);
            }

            // Disable the built-in global UnsupportedContentTypeFilter
            // We enable our own ValidateContentTypeFilterAttribute on the FhirController, the built-in filter
            // short-circuits the response and prevents the operation outcome from being returned.
            var unsupportedContentTypeFilter = options.Filters.Single(x => x is UnsupportedContentTypeFilter);
            options.Filters.Remove(unsupportedContentTypeFilter);
        }

        public void Build(ListedCapabilityStatement statement)
        {
            if (_featureConfiguration.SupportsXml)
            {
                statement.Format.Add(ContentType.XML_CONTENT_HEADER);
            }
        }
    }
}
