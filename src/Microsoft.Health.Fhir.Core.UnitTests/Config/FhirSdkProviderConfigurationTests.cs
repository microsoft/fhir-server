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
        private const string Section = "FhirServer:CoreFeatures:FhirSdkProvider";

        [Fact]
        public void GivenDefaultConfiguration_WhenProviderRead_ThenFirelyIsSelected()
        {
            var configuration = new CoreFeatureConfiguration();

            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.SystemDefault);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Fact]
        public void GivenSystemDefault_WhenBound_ThenEverySeamUsesIt()
        {
            FhirSdkProviderConfiguration configuration = Bind(new Dictionary<string, string>
            {
                [$"{Section}:SystemDefault"] = "Ignixa",
            });

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.EffectiveFhirPath);
        }

        [Fact]
        public void GivenSeamOverride_WhenBound_ThenOnlyThatSeamChanges()
        {
            FhirSdkProviderConfiguration configuration = Bind(new Dictionary<string, string>
            {
                [$"{Section}:SystemDefault"] = "Ignixa",
                [$"{Section}:FhirPath"] = "Firely",
            });

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.EffectiveFhirPath);
        }

        [Theory]
        [InlineData("Ignixa", "FhirPath", "Firely")]
        [InlineData("Firely", "FhirPath", "Ignixa")]
        [InlineData("Ignixa", "Import", "Firely")]
        [InlineData("Firely", "Import", "Ignixa")]
        public void GivenSystemDefaultAndOverrideFromSeparateSources_WhenBound_ThenOverrideWins(
            string systemDefault,
            string seam,
            string seamProvider)
        {
            FhirSdkProviderConfiguration configuration = Bind(
                new Dictionary<string, string> { [$"{Section}:SystemDefault"] = systemDefault },
                new Dictionary<string, string> { [$"{Section}:{seam}"] = seamProvider });

            FhirSdkProvider expectedDefault = Enum.Parse<FhirSdkProvider>(systemDefault);
            FhirSdkProvider expectedSeam = Enum.Parse<FhirSdkProvider>(seamProvider);
            Assert.Equal(seam == "FhirPath" ? expectedSeam : expectedDefault, configuration.EffectiveFhirPath);
            Assert.Equal(seam == "Import" ? expectedSeam : expectedDefault, configuration.EffectiveImport);
        }

        [Fact]
        public void GivenCaseInsensitiveKeysAndValues_WhenBound_ThenTheyApply()
        {
            FhirSdkProviderConfiguration configuration = Bind(new Dictionary<string, string>
            {
                [$"{Section}:systemdefault"] = "ignixa",
                [$"{Section}:FHIRPATH"] = "FIRELY",
            });

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.EffectiveFhirPath);
        }

        [Fact]
        public void GivenSingleProviderValue_WhenValidated_ThenStartupFails()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Bind(
                new Dictionary<string, string> { [Section] = "Ignixa" }));

            Assert.Contains($"{Section}:SystemDefault", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenSingleProviderValueAndOverrideFromSeparateSources_WhenValidated_ThenStartupFails()
        {
            Assert.Throws<InvalidOperationException>(() => Bind(
                new Dictionary<string, string> { [$"{Section}:FhirPath"] = "Firely" },
                new Dictionary<string, string> { [Section] = "Ignixa" }));
        }

        [Theory]
        [InlineData("Default")]
        [InlineData("FhirPth")]
        public void GivenUnknownSetting_WhenValidated_ThenStartupFails(string key)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Bind(
                new Dictionary<string, string> { [$"{Section}:{key}"] = "Ignixa" }));

            Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("SystemDefault", "Bogus")]
        [InlineData("SystemDefault", "1")]
        [InlineData("FhirPath", "5")]
        [InlineData("Import", "")]
        [InlineData("SystemDefault", "Firely, Ignixa")]
        [InlineData("FhirPath", "Firely,Ignixa")]
        [InlineData("Import", "Ignixa, Firely")]
        public void GivenInvalidProviderValue_WhenValidated_ThenStartupFails(string key, string value)
        {
            Assert.Throws<InvalidOperationException>(() => Bind(
                new Dictionary<string, string> { [$"{Section}:{key}"] = value }));
        }

        // Mirrors AddFhirServer: validate the provider section, then bind the configuration.
        private static FhirSdkProviderConfiguration Bind(params IDictionary<string, string>[] sources)
        {
            var builder = new ConfigurationBuilder();
            foreach (IDictionary<string, string> source in sources)
            {
                builder.AddInMemoryCollection(source);
            }

            IConfigurationRoot root = builder.Build();
            FhirSdkProviderConfiguration.Validate(root.GetSection(Section));

            var configuration = new CoreFeatureConfiguration();
            root.GetSection("FhirServer:CoreFeatures").Bind(configuration);
            return configuration.FhirSdkProvider;
        }
    }
}
