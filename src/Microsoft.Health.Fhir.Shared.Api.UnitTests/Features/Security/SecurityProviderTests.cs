// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Providers;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SecurityProviderTests
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IWellKnownConfigurationProvider _wellKnownConfigurationProvider;
        private readonly IUrlResolver _urlResolver;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ICapabilityStatementBuilder _builder;
        private readonly ListedCapabilityStatement _capabilityStatement = new ListedCapabilityStatement();
        private readonly SecurityProvider _securityProvider;

        public SecurityProviderTests()
        {
            _securityConfiguration = new SecurityConfiguration();
            _securityConfiguration.Enabled = true;
            _wellKnownConfigurationProvider = Substitute.For<IWellKnownConfigurationProvider>();
            _urlResolver = Substitute.For<IUrlResolver>();
            _modelInfoProvider = Substitute.For<IModelInfoProvider>();

            var component = new ListedRestComponent
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };
            _capabilityStatement = new ListedCapabilityStatement();
            _capabilityStatement.Rest.Add(component);

            _builder = Substitute.For<ICapabilityStatementBuilder>();
            _builder.When(x => x.Apply(Arg.Any<Action<ListedCapabilityStatement>>())).Do(x => ((Action<ListedCapabilityStatement>)x[0])(_capabilityStatement));

            _securityProvider = new SecurityProvider(
                Options.Create(_securityConfiguration),
                _wellKnownConfigurationProvider,
                NullLogger<SecurityProvider>.Instance,
                _urlResolver,
                _modelInfoProvider);
        }

        [Fact]
        public void GivenSecurityNotEnabled_WhenBuildCalled_SecurityNotAddedToBuilder()
        {
            _securityConfiguration.Enabled = false;
            _securityProvider.Build(_builder);

            _builder.DidNotReceive().Apply(Arg.Any<Action<ListedCapabilityStatement>>());
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(null, "https://testhost:44312/token")]
        [InlineData("https://testhost:44312/auth", null)]
        public void GivenOAuthAuthorizationEndpointOrTokenEndpointIsInvalid_WhenBuildCalled_ExceptionThrown(string auth, string token)
        {
            _wellKnownConfigurationProvider.GetOpenIdConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetOpenIdConfiguration(auth, token));

            Assert.Throws<OpenIdConfigurationException>(() => _securityProvider.Build(_builder));
        }

        [Fact]
        public void GivenOpenIdConfigurationProvided_WhenBuildCalled_ExpectedCapabilitiesAreAdded()
        {
            string auth = "https://testhost:44312/auth";
            string token = "https://testhost:44312/token";

            _modelInfoProvider.Version.Returns(FhirSpecification.R4);
            _wellKnownConfigurationProvider.IsSmartConfigured().Returns(false);
            _wellKnownConfigurationProvider.GetOpenIdConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetOpenIdConfiguration(auth, token));

            _securityProvider.Build(_builder);

            SecurityComponent security = _capabilityStatement.Rest?.FirstOrDefault()?.Security;
            Assert.NotNull(security);

            CodableConceptInfo service = security.Service.FirstOrDefault();
            Assert.Single(security.Service);
            Assert.Single(service.Coding);
            Assert.Equal("http://terminology.hl7.org/CodeSystem/restful-security-service", service.Coding.First().System);
            Assert.Equal("OAuth", service.Coding.First().Code);

            JObject extension = security.Extension.FirstOrDefault();
            Assert.Single(security.Extension);
            Assert.Equal("http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris", extension["url"]);
            VerifyExtension("authorize", auth, (JArray)extension["extension"]);
            VerifyExtension("token", token, (JArray)extension["extension"]);
        }

        [Theory]
        [InlineData("https://testhost:44312/auth", "https://testhost:44312/token", null, null, null, null)]
        [InlineData("https://testhost:44312/auth", "https://testhost:44312/token", "https://testhost:44312/reg", null, null, null)]
        [InlineData("https://testhost:44312/auth", "https://testhost:44312/token", "https://testhost:44312/reg", "https://testhost:44312/man", null, null)]
        [InlineData("https://testhost:44312/auth", "https://testhost:44312/token", "https://testhost:44312/reg", "https://testhost:44312/man", "https://testhost:44312/intr", null)]
        [InlineData("https://testhost:44312/auth", "https://testhost:44312/token", "https://testhost:44312/reg", "https://testhost:44312/man", "https://testhost:44312/intr", "https://testhost:44312/rev")]
        public void GivenSmartConfigurationProvided_WhenBuildCalled_ExpectedCapabilitiesAreAdded(string auth, string token, string reg, string man, string intr, string rev)
        {
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);
            _wellKnownConfigurationProvider.IsSmartConfigured().Returns(true);
            _wellKnownConfigurationProvider.GetSmartConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetSmartConfiguration(auth, token, reg, man, intr, rev));

            _securityProvider.Build(_builder);

            SecurityComponent security = _capabilityStatement.Rest?.FirstOrDefault()?.Security;
            Assert.NotNull(security);

            CodableConceptInfo service = security.Service.FirstOrDefault();
            Assert.Single(security.Service);
            Assert.Single(service.Coding);
            Assert.Equal("http://terminology.hl7.org/CodeSystem/restful-security-service", service.Coding.First().System);
            Assert.Equal("SMART-on-FHIR", service.Coding.First().Code);

            JObject extension = security.Extension.FirstOrDefault();
            Assert.Single(security.Extension);
            Assert.Equal("http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris", extension["url"]);
            VerifyExtension("authorize", auth, (JArray)extension["extension"]);
            VerifyExtension("token", token, (JArray)extension["extension"]);
            VerifyExtension("register", reg, (JArray)extension["extension"]);
            VerifyExtension("manage", man, (JArray)extension["extension"]);
            VerifyExtension("revoke", rev, (JArray)extension["extension"]);
            VerifyExtension("introspect", intr, (JArray)extension["extension"]);
        }

        [Fact]
        public void GivenSmartConfigurationIsNotValid_WhenBuildCalled_FallbackCapabilitiesAreAdded()
        {
            string auth = "https://testhost:44312/auth";
            string token = "https://testhost:44312/token";

            _modelInfoProvider.Version.Returns(FhirSpecification.R4);
            _wellKnownConfigurationProvider.IsSmartConfigured().Returns(true);
            _wellKnownConfigurationProvider.GetSmartConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetSmartConfiguration(null, null));
            _wellKnownConfigurationProvider.GetOpenIdConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetOpenIdConfiguration(auth, token));

            _securityProvider.Build(_builder);

            SecurityComponent security = _capabilityStatement.Rest?.FirstOrDefault()?.Security;
            Assert.NotNull(security);

            CodableConceptInfo service = security.Service.FirstOrDefault();
            Assert.Single(security.Service);
            Assert.Single(service.Coding);
            Assert.Equal("http://terminology.hl7.org/CodeSystem/restful-security-service", service.Coding.First().System);
            Assert.Equal("OAuth", service.Coding.First().Code);

            JObject extension = security.Extension.FirstOrDefault();
            Assert.Single(security.Extension);
            Assert.Equal("http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris", extension["url"]);
            VerifyExtension("authorize", auth, (JArray)extension["extension"]);
            VerifyExtension("token", token, (JArray)extension["extension"]);
        }

        private void VerifyExtension(string expectedUrl, string expectedValueUri, JArray extension)
        {
            if (string.IsNullOrWhiteSpace(expectedValueUri))
            {
                return;
            }

            List<Dictionary<string, string>> values = extension.ToObject<List<Dictionary<string, string>>>();
            Dictionary<string, string> value = values.FirstOrDefault(x => string.Equals(expectedValueUri, x["valueUri"], StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(value);
            Assert.Equal(2, value.Count);
            Assert.Equal(expectedUrl, value["url"]);
            Assert.Equal(expectedValueUri, value["valueUri"]);
        }

        private OpenIdConfigurationResponse GetOpenIdConfiguration(string authorization, string token)
        {
            return new OpenIdConfigurationResponse(authorization != null ? new Uri(authorization) : null, token != null ? new Uri(token) : null);
        }

        private GetSmartConfigurationResponse GetSmartConfiguration(
            string authorization,
            string token,
            string registration = null,
            string management = null,
            string introspection = null,
            string revocation = null)
        {
            return new GetSmartConfigurationResponse(
                null,
                null,
                authorization != null ? new Uri(authorization) : null,
                null,
                token != null ? new Uri(token) : null,
                null,
                registration != null ? new Uri(registration) : null,
                null,
                null,
                null,
                null,
                null,
                management != null ? new Uri(management) : null,
                introspection != null ? new Uri(introspection) : null,
                revocation != null ? new Uri(revocation) : null,
                null,
                null);
        }
    }
}
