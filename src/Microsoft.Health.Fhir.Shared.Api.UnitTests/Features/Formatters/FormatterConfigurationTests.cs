// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    public class FormatterConfigurationTests
    {
        private readonly FeatureConfiguration _featureConfiguration = new FeatureConfiguration();
        private readonly FhirJsonInputFormatter _fhirJsonInputFormatter = new FhirJsonInputFormatter(new FhirJsonParser(), ArrayPool<char>.Shared);
        private readonly FhirXmlInputFormatter _fhirXmlInputFormatter = new FhirXmlInputFormatter(new FhirXmlParser());
        private readonly FhirXmlOutputFormatter _fhirXmlOutputFormatter = new FhirXmlOutputFormatter(new FhirXmlSerializer(), NullLogger<FhirXmlOutputFormatter>.Instance);
        private readonly HtmlOutputFormatter _htmlOutputFormatter;
        private readonly FhirJsonOutputFormatter _fhirJsonOutputFormatter;
        private readonly FormatterConfiguration _configuration;
        private readonly MvcOptions _options;

        public FormatterConfigurationTests()
        {
            var serializer = new FhirJsonSerializer();

            _htmlOutputFormatter = new HtmlOutputFormatter(serializer, NullLogger<HtmlOutputFormatter>.Instance, new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance), ArrayPool<char>.Shared);
            _fhirJsonOutputFormatter = new FhirJsonOutputFormatter(serializer, NullLogger<FhirJsonOutputFormatter>.Instance, ArrayPool<char>.Shared);

            _configuration = new FormatterConfiguration(
                Options.Create(_featureConfiguration),
                Substitute.For<IConfiguredConformanceProvider>(),
                new TextInputFormatter[] { _fhirJsonInputFormatter, _fhirXmlInputFormatter },
                new TextOutputFormatter[] { _htmlOutputFormatter, _fhirJsonOutputFormatter, _fhirXmlOutputFormatter });

            _options = new MvcOptions();
            _options.Filters.Add(new UnsupportedContentTypeFilter());
        }

        [Fact]
        public void GivenSupportedFeatures_WhenConfigured_ThenCorrectOutputFormattersShouldBeAdded()
        {
            _configuration.PostConfigure("test", _options);

            Assert.Collection(
                _options.OutputFormatters,
                f => Assert.Equal(_htmlOutputFormatter, f),
                f => Assert.Equal(_fhirJsonOutputFormatter, f),
                f => Assert.Equal(_fhirXmlOutputFormatter, f));
        }

        [Fact]
        public void GivenTheDefaultOptions_WhenConfigured_ThenBuiltInUnsupportedContentTypeFilterIsRemoved()
        {
            _configuration.PostConfigure("test", _options);

            Assert.Empty(_options.Filters);
        }
    }
}
