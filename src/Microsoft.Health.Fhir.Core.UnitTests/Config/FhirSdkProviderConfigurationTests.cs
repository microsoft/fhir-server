// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirSdkProviderConfigurationTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenProviderRead_ThenFirelyIsSelected()
        {
            var configuration = new CoreFeatureConfiguration();
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Fact]
        public void GivenIgnixaConfigured_WhenProviderRead_ThenIgnixaIsSelected()
        {
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration
                {
                    Default = FhirSdkProvider.Ignixa,
                },
            };
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Fact]
        public void GivenSeamOverrides_WhenProvidersRead_ThenSelectionsAreIndependent()
        {
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration
                {
                    Default = FhirSdkProvider.Firely,
                    Import = FhirSdkProvider.Ignixa,
                },
            };

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Theory]
        [InlineData("Ignixa", FhirSdkProvider.Ignixa)]
        [InlineData("ignixa", FhirSdkProvider.Ignixa)]
        [InlineData("Firely", FhirSdkProvider.Firely)]
        public void GivenLegacyScalarSetting_WhenBound_ThenItSelectsTheDefaultProvider(string value, FhirSdkProvider expected)
        {
            CoreFeatureConfiguration configuration = Bind(new Dictionary<string, string>
            {
                ["CoreFeatures:FhirSdkProvider"] = value,
            });

            Assert.Equal(expected, configuration.FhirSdkProvider.Default);
            Assert.Null(configuration.FhirSdkProvider.Import);
            Assert.Null(configuration.FhirSdkProvider.FhirPath);
            Assert.Equal(expected, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(expected, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Fact]
        public void GivenPerSeamSettings_WhenBound_ThenOverridesApply()
        {
            CoreFeatureConfiguration configuration = Bind(new Dictionary<string, string>
            {
                ["CoreFeatures:FhirSdkProvider:Default"] = "Ignixa",
                ["CoreFeatures:FhirSdkProvider:FhirPath"] = "Firely",
            });

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Theory]
        [InlineData("Bogus")]
        [InlineData("1")]
        [InlineData("")]
        public void GivenInvalidLegacyScalarSetting_WhenBound_ThenBindingFails(string value)
        {
            Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string>
            {
                ["CoreFeatures:FhirSdkProvider"] = value,
            }));
        }

        private static CoreFeatureConfiguration Bind(IDictionary<string, string> settings)
        {
            IConfiguration root = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var configuration = new CoreFeatureConfiguration();
            root.GetSection("CoreFeatures").Bind(configuration);
            return configuration;
        }
    }
}
