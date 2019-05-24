// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Configs;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Cors
{
    public class CorsModuleTests
    {
        private readonly CorsModule _corsModule;
        private readonly CorsConfiguration _corsConfiguration = Substitute.For<CorsConfiguration>();
        private readonly IServiceCollection _servicesCollection = Substitute.For<IServiceCollection>();

        public CorsModuleTests()
        {
            var fhirServerConfiguration = Substitute.For<FhirServerConfiguration>();

            fhirServerConfiguration.Cors.Returns(_corsConfiguration);

            _corsModule = new CorsModule(fhirServerConfiguration);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenNoValuesSet_PolicyHasOnlyDefaults()
        {
            _corsModule.Load(_servicesCollection);

            CorsPolicy corsPolicy = _corsModule.DefaultCorsPolicy;
            Assert.Empty(corsPolicy.Origins);
            Assert.Empty(corsPolicy.Headers);
            Assert.Empty(corsPolicy.Methods);
            Assert.False(corsPolicy.SupportsCredentials);
            Assert.Null(corsPolicy.PreflightMaxAge);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenAllOriginsSet_PolicyHasAllowAnyOrigin()
        {
            _corsConfiguration.Origins.Add("*");
            _corsModule.Load(_servicesCollection);

            Assert.True(_corsModule.DefaultCorsPolicy.AllowAnyOrigin);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenAllMethodsSet_PolicyHasAllowAnyMethod()
        {
            _corsConfiguration.Methods.Add("*");
            _corsModule.Load(_servicesCollection);

            Assert.True(_corsModule.DefaultCorsPolicy.AllowAnyMethod);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenAllHeadersSet_PolicyHasAllowAnyHeader()
        {
            _corsConfiguration.Headers.Add("*");
            _corsModule.Load(_servicesCollection);

            Assert.True(_corsModule.DefaultCorsPolicy.AllowAnyHeader);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenAllowCredentials_PolicyHasSupportsCredentials()
        {
            _corsConfiguration.AllowCredentials = true;
            _corsModule.Load(_servicesCollection);

            Assert.True(_corsModule.DefaultCorsPolicy.SupportsCredentials);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenMaxAgeSet_PolicyHasMaxAge()
        {
            _corsConfiguration.MaxAge = 100;
            _corsModule.Load(_servicesCollection);

            Assert.Equal(TimeSpan.FromSeconds(100), _corsModule.DefaultCorsPolicy.PreflightMaxAge);
        }

        [Fact]
        public void GivenACorsConfiguration_WhenMultipleValuesSet_PolicyHasSpecifiedValues()
        {
            _corsConfiguration.Origins.Add("https://example.com");
            _corsConfiguration.Origins.Add("https://contoso");

            _corsConfiguration.Methods.Add("PATCH");
            _corsConfiguration.Methods.Add("DELETE");

            _corsConfiguration.Headers.Add("authorization");
            _corsConfiguration.Headers.Add("content-type");

            _corsModule.Load(_servicesCollection);

            Assert.Equal(2, _corsModule.DefaultCorsPolicy.Origins.Count);
            Assert.Equal(2, _corsModule.DefaultCorsPolicy.Methods.Count);
            Assert.Equal(2, _corsModule.DefaultCorsPolicy.Headers.Count);

            Assert.Contains("https://example.com", _corsModule.DefaultCorsPolicy.Origins);
            Assert.Contains("https://contoso", _corsModule.DefaultCorsPolicy.Origins);

            Assert.Contains("PATCH", _corsModule.DefaultCorsPolicy.Methods);
            Assert.Contains("DELETE", _corsModule.DefaultCorsPolicy.Methods);

            Assert.Contains("authorization", _corsModule.DefaultCorsPolicy.Headers);
            Assert.Contains("content-type", _corsModule.DefaultCorsPolicy.Headers);
        }
    }
}
