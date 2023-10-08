// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class EncryptionConfigurationTests
    {
        [Fact]
        public void GivenAnEncryptionConfiguration_WhenContainsValues_ReturnAsEncryptionConfigurationSet()
        {
            EncryptionConfiguration encryption0 = new EncryptionConfiguration()
            {
                CustomerManagedKeyEncryption = new CustomerManagedKeyEncryption()
                {
                    KeyEncryptionKeyIdentity = new EncryptionKeyIdentity()
                    {
                        FederatedClientId = "food",
                    },
                },
            };
            Assert.True(encryption0.IsEncryptionSet());

            EncryptionConfiguration encryption1 = new EncryptionConfiguration()
            {
                CustomerManagedKeyEncryption = new CustomerManagedKeyEncryption()
                {
                    KeyEncryptionKeyUrl = new Uri("htts://fhir.com"),
                },
            };
            Assert.True(encryption1.IsEncryptionSet());
        }

        [Fact]
        public void GivenAnEncryptionConfiguration_WhenThereAreNoValues_ReturnAsEncryptionConfigurationNotSet()
        {
            EncryptionConfiguration encryption0 = new EncryptionConfiguration();
            Assert.False(encryption0.IsEncryptionSet());

            EncryptionConfiguration encryption1 = new EncryptionConfiguration()
            {
                CustomerManagedKeyEncryption = new CustomerManagedKeyEncryption()
                {
                    KeyEncryptionKeyIdentity = new EncryptionKeyIdentity()
                    {
                        FederatedClientId = null,
                    },
                },
            };
            Assert.False(encryption1.IsEncryptionSet());

            EncryptionConfiguration encryption2 = new EncryptionConfiguration()
            {
                CustomerManagedKeyEncryption = new CustomerManagedKeyEncryption()
                {
                    KeyEncryptionKeyUrl = null,
                },
            };
            Assert.False(encryption2.IsEncryptionSet());

            EncryptionConfiguration encryption3 = new EncryptionConfiguration()
            {
                CustomerManagedKeyEncryption = null,
            };
            Assert.False(encryption3.IsEncryptionSet());
        }
    }
}
