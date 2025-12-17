// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Multitenant.Core;
using Xunit;

namespace Microsoft.Health.Fhir.Multitenant.Core.UnitTests;

public class TenantConfigurationTests
{
    [Fact]
    public void TenantConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new TenantConfiguration();

        // Assert
        Assert.Equal(string.Empty, config.TenantId);
        Assert.Equal(0, config.Port);
        Assert.Equal(string.Empty, config.ConnectionString);
        Assert.NotNull(config.Settings);
        Assert.Empty(config.Settings);
    }

    [Fact]
    public void TenantConfiguration_CanSetProperties()
    {
        // Arrange
        var config = new TenantConfiguration
        {
            TenantId = "test-tenant",
            Port = 5001,
            ConnectionString = "Server=test;Database=db",
            Settings = new Dictionary<string, string?>
            {
                ["FhirServer:Security:Enabled"] = "false",
            },
        };

        // Assert
        Assert.Equal("test-tenant", config.TenantId);
        Assert.Equal(5001, config.Port);
        Assert.Equal("Server=test;Database=db", config.ConnectionString);
        Assert.Single(config.Settings);
        Assert.Equal("false", config.Settings["FhirServer:Security:Enabled"]);
    }
}
