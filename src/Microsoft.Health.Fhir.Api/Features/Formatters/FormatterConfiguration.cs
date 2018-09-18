// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
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
        private readonly IEnumerable<TextInputFormatter> _inputFormatters;
        private readonly IEnumerable<TextOutputFormatter> _outputFormatters;

        public FormatterConfiguration(
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
            _inputFormatters = inputFormatters;
            _outputFormatters = outputFormatters;
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            foreach (var formatter in _inputFormatters)
            {
                options.InputFormatters.Add(formatter);
            }

            foreach (var formatter in _outputFormatters)
            {
                options.OutputFormatters.Add(formatter);
            }

            if (_featureConfiguration.SupportsXml)
            {
                // TODO: This feature flag should be removed when we support custom capability statements
                _configuredConformanceProvider.ConfigureOptionalCapabilities(statement => statement.Format = statement.Format.Concat(new[] { ContentType.XML_CONTENT_HEADER }));
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
