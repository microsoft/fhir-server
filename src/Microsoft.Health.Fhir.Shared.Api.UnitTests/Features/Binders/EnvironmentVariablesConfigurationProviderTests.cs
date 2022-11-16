// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Binders;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Binders
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class EnvironmentVariablesConfigurationProviderTests
    {
        [Fact]
        public void Test()
        {
            MockConfigurationProvider mockProvider = new MockConfigurationProvider(
                new Dictionary<string, string>()
                {
                    { "FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides", "{ \"visionprescription\": \"versioned\" }" },
                });

            EnvironmentVariablesDictionaryConfigurationProvider configurationProvider = new EnvironmentVariablesDictionaryConfigurationProvider(mockProvider);

            configurationProvider.Load();

            Assert.True(configurationProvider.TryGet("FhirServer:CoreFeatures:Versioning:ResourceTypeOverrides:visionprescription", out string visionPrescriptionValue));
            Assert.Equal("versioned", visionPrescriptionValue);
        }

        [Fact]
        public void Test2()
        {
            var environmentVariables = Environment.GetEnvironmentVariables();
            var environmentVariablesAsDictionary = new Dictionary<string, string>();

            IDictionaryEnumerator e = environmentVariables.GetEnumerator();
            while (e.MoveNext())
            {
                DictionaryEntry entry = e.Entry;
                string key = (string)entry.Key;
                environmentVariablesAsDictionary.Add((string)entry.Key, (string)entry.Value);
            }

            MockConfigurationProvider mockProvider = new MockConfigurationProvider(environmentVariablesAsDictionary);
            EnvironmentVariablesDictionaryConfigurationProvider configurationProvider = new EnvironmentVariablesDictionaryConfigurationProvider(mockProvider);
            configurationProvider.Load();

            EnvironmentVariablesDictionaryConfigurationProvider defaultConfigurationProvider = new EnvironmentVariablesDictionaryConfigurationProvider();
            defaultConfigurationProvider.Load();

            foreach (string key in environmentVariables.Keys)
            {
                Assert.True(configurationProvider.TryGet(key, out string value1));
                Assert.True(defaultConfigurationProvider.TryGet(key, out string value2));

                Assert.Equal(value1, value2);
            }
        }

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
