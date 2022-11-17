// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Binders;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Binders
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class EnvironmentVariablesConfigurationProviderTests
    {
        [Fact]
        public void GivenAMockConfigurationProvider_WhenInitialized_ThenEnsureAllCustomEnvironmentVariablesAreIncluded()
        {
            MockConfigurationProvider mockProvider = new MockConfigurationProvider(
                new Dictionary<string, string>()
                {
                    { "FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides", "{ \"visionprescription\": \"versioned\" }" },
                });

            EnvironmentVariablesDictionaryConfigurationProvider configurationProvider = new EnvironmentVariablesDictionaryConfigurationProvider(mockProvider);

            configurationProvider.Load();

            Assert.Single(configurationProvider.GetChildKeys(Array.Empty<string>(), null));
            Assert.True(configurationProvider.TryGet("FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides:visionprescription", out string visionPrescriptionValue));
            Assert.Equal("versioned", visionPrescriptionValue);
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

                if (HasJsonStructure(value))
                {
                    Dictionary<string, string> asDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(value);
                    variablesInJsonFormat += asDictionary.Count;
                }
            }

            EnvironmentVariablesDictionaryConfigurationProvider defaultConfigurationProvider = new EnvironmentVariablesDictionaryConfigurationProvider();
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

                if (HasJsonStructure(value))
                {
                    Dictionary<string, string> asDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(value);
                    variablesInJsonFormat += asDictionary.Count;
                }
            }

            MockConfigurationProvider mockProviderWithAllEnvironmentVariables = new MockConfigurationProvider(environmentVariablesAsDictionary);
            mockProviderWithAllEnvironmentVariables.Load();

            int mockProviderTotalNumberOfVariables = mockProviderWithAllEnvironmentVariables.GetChildKeys(Array.Empty<string>(), null).Count();
            Assert.Equal(variablesCount, mockProviderTotalNumberOfVariables);

            EnvironmentVariablesDictionaryConfigurationProvider mockConfigurationProvider = new EnvironmentVariablesDictionaryConfigurationProvider(mockProviderWithAllEnvironmentVariables);
            mockConfigurationProvider.Load();

            int numberOfJsonVariablesInMockProvider = mockConfigurationProvider.GetChildKeys(Array.Empty<string>(), null).Count();
            Assert.Equal(variablesInJsonFormat, numberOfJsonVariablesInMockProvider);
        }

        private static bool HasJsonStructure(string value) => value.Trim().StartsWith("{", StringComparison.Ordinal);

        public sealed class MockConfigurationProvider : IConfigurationProvider
        {
            public MockConfigurationProvider(IDictionary<string, string> data)
            {
                Data = data;
            }

            private IDictionary<string, string> Data { get; }

            public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string parentPath) => Data.Keys;

            public IChangeToken GetReloadToken()
            {
                throw new System.NotImplementedException();
            }

            public void Load()
            {
            }

            public void Set(string key, string value) => Data.Add(key, value);

            public bool TryGet(string key, out string value) => Data.TryGetValue(key, out value);
        }
    }
}
