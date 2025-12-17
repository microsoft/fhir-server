// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Multitenant.Core;
using Xunit;

namespace Microsoft.Health.Fhir.Multitenant.Core.UnitTests;

public class MultitenantSettingsTests
{
    [Fact]
    public void MultitenantSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new MultitenantSettings();

        // Assert
        Assert.Equal(8080, settings.RouterPort);
        Assert.NotNull(settings.Tenants);
        Assert.Empty(settings.Tenants);
    }

    [Fact]
    public void MultitenantSettings_SectionName_IsCorrect()
    {
        // Assert
        Assert.Equal("Multitenant", MultitenantSettings.SectionName);
    }

    [Fact]
    public void MultitenantSettings_CanSetProperties()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            RouterPort = 9090,
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-1", Port = 5001 },
                new TenantConfiguration { TenantId = "tenant-2", Port = 5002 },
            ],
        };

        // Assert
        Assert.Equal(9090, settings.RouterPort);
        Assert.Equal(2, settings.Tenants.Count);
        Assert.Equal("tenant-1", settings.Tenants[0].TenantId);
        Assert.Equal("tenant-2", settings.Tenants[1].TenantId);
    }
}
