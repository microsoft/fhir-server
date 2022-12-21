﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Health.Fhir.Api.Features.Binders;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Binders
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class DictionaryExpansionConfigurationProviderTests
    {
        [Fact]
        public void GivenAMockConfigurationProvider_WhenInitialized_ThenEnsureAllCustomEnvironmentVariablesAreIncluded()
        {
            MockConfigurationProvider mockProvider = new MockConfigurationProvider(
                new Dictionary<string, string>()
                {
                    { "FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides1", "{ \"visionprescription\": \"versioned\" }" },
                    { "FhirServer", "{ \"visionprescription\": \"versioned\" }" },
                    { "FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides2", "{ \"account\": \"no-version\", \"visionprescription\": \"versioned\", \"activitydefinition\": \"versioned-update\" }" },
                });

            DictionaryExpansionConfigurationProvider configurationProvider = new DictionaryExpansionConfigurationProvider(mockProvider);

            configurationProvider.Load();

            Assert.Equal(expected: 5, configurationProvider.GetChildKeys(Array.Empty<string>(), null).Count());

            Assert.True(configurationProvider.TryGet("FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides1:visionprescription", out string visionPrescriptionValue1));
            Assert.Equal("versioned", visionPrescriptionValue1);

            Assert.True(configurationProvider.TryGet("FhirServer:visionprescription", out string visionPrescriptionValue2));
            Assert.Equal("versioned", visionPrescriptionValue2);

            Assert.True(configurationProvider.TryGet("FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides2:account", out string accountValue1));
            Assert.Equal("no-version", accountValue1);

            Assert.True(configurationProvider.TryGet("FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides2:visionprescription", out string visionPrescriptionValue3));
            Assert.Equal("versioned", visionPrescriptionValue3);

            Assert.True(configurationProvider.TryGet("FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides2:activitydefinition", out string activityDefinitionValue1));
            Assert.Equal("versioned-update", activityDefinitionValue1);
        }

        [Fact]
        public void GivenARealConfigurationProvider_WhenInitialized_ThenEnsureJustVariablesWithJsonAreReturned()
        {
            var environmentVariables = Environment.GetEnvironmentVariables();
            IDictionaryEnumerator e = environmentVariables.GetEnumerator();

            var environmentVariablesAsDictionary = new Dictionary<string, string>();
            int variablesInJsonFormat = 0;
            while (e.MoveNext())
            {
                DictionaryEntry entry = e.Entry;
                string key = (string)entry.Key;
                string value = (string)entry.Value;
                environmentVariablesAsDictionary.Add(key, value);

                if (DictionaryExpansionConfigurationProvider.TryParseDictionaryJson(value, out Dictionary<string, string> asDictionary))
                {
                    variablesInJsonFormat += asDictionary.Count;
                }
            }

            DictionaryExpansionConfigurationProvider defaultConfigurationProvider = new DictionaryExpansionConfigurationProvider(new EnvironmentVariablesConfigurationProvider());
            defaultConfigurationProvider.Load();

            int numberOfJsonVariablesInDefaultProvider = defaultConfigurationProvider.GetChildKeys(Array.Empty<string>(), null).Count();
            Assert.Equal(variablesInJsonFormat, numberOfJsonVariablesInDefaultProvider);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenAMockConfigurationProvider_WhenInitialized_ThenEnsureJustVariablesWithJsonAreReturned(bool loadLocalEnvironmentVariablesInsteadOfFakeOnes)
        {
            IDictionaryEnumerator e = null;

            if (loadLocalEnvironmentVariablesInsteadOfFakeOnes)
            {
                var environmentVariables = Environment.GetEnvironmentVariables();
                e = environmentVariables.GetEnumerator();
            }
            else
            {
                var customVariables = new Dictionary<string, string>()
                {
                    { "FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides", "{ \"account\": \"no-version\", \"visionprescription\": \"versioned\", \"activitydefinition\": \"versioned-update\" }" },
                };
                e = customVariables.GetEnumerator();
            }

            var environmentVariablesAsDictionary = new Dictionary<string, string>();
            int variablesInJsonFormat = 0;
            int variablesCount = 0;
            while (e.MoveNext())
            {
                variablesCount++;

                DictionaryEntry entry = e.Entry;
                string key = (string)entry.Key;
                string value = (string)entry.Value;
                environmentVariablesAsDictionary.Add(key, value);

                if (DictionaryExpansionConfigurationProvider.TryParseDictionaryJson(value, out Dictionary<string, string> asDictionary))
                {
                    variablesInJsonFormat += asDictionary.Count;
                }
            }

            MockConfigurationProvider mockProviderWithAllEnvironmentVariables = new MockConfigurationProvider(environmentVariablesAsDictionary);
            mockProviderWithAllEnvironmentVariables.Load();

            int mockProviderTotalNumberOfVariables = mockProviderWithAllEnvironmentVariables.GetChildKeys(Array.Empty<string>(), null).Count();
            Assert.Equal(variablesCount, mockProviderTotalNumberOfVariables);

            DictionaryExpansionConfigurationProvider mockConfigurationProvider = new DictionaryExpansionConfigurationProvider(mockProviderWithAllEnvironmentVariables);
            mockConfigurationProvider.Load();

            int numberOfJsonVariablesInMockProvider = mockConfigurationProvider.GetChildKeys(Array.Empty<string>(), null).Count();
            Assert.Equal(variablesInJsonFormat, numberOfJsonVariablesInMockProvider);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("{ {")]
        [InlineData("{ { } }")]
        [InlineData(" {} ")]
        [InlineData(" { } ")]
        [InlineData("{foo}")]
        [InlineData(" { foo } ")]
        [InlineData("{ 'foo' }")]
        [InlineData("{ \"foo\" }")]
        [InlineData("{070741cc-f9e9-516d-9f32-f3a4cca9614d}")]
        public void GivenAnInvalidJsonConfiguration_WhenInitialized_ThenKeepValuesAsTheyAre(string value)
        {
            MockConfigurationProvider mockProvider = new MockConfigurationProvider(
                new Dictionary<string, string>()
                {
                    { "key", value},
                });

            DictionaryExpansionConfigurationProvider configurationProvider = new DictionaryExpansionConfigurationProvider(mockProvider);

            configurationProvider.Load();

            Assert.Empty(configurationProvider.GetChildKeys(Array.Empty<string>(), null));
        }

        public sealed class MockConfigurationProvider : ConfigurationProvider
        {
            private readonly IDictionary<string, string> _data;

            public MockConfigurationProvider(IDictionary<string, string> data)
            {
                _data = data;
            }

            public override void Load()
            {
                Data = _data;
            }
        }
    }
}
