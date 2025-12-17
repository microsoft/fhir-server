// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Multitenant.Core;
using Xunit;

namespace Microsoft.Health.Fhir.Multitenant.Core.UnitTests;

public class TenantManagerTests
{
    [Fact]
    public void GetTenantPort_WithValidTenant_ReturnsPort()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-a", Port = 5001 },
                new TenantConfiguration { TenantId = "tenant-b", Port = 5002 },
            ],
        };

        var manager = new TenantManager(settings);

        // Act
        var port = manager.GetTenantPort("tenant-a");

        // Assert
        Assert.Equal(5001, port);
    }

    [Fact]
    public void GetTenantPort_WithInvalidTenant_ReturnsNull()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-a", Port = 5001 },
            ],
        };

        var manager = new TenantManager(settings);

        // Act
        var port = manager.GetTenantPort("invalid-tenant");

        // Assert
        Assert.Null(port);
    }

    [Fact]
    public void GetTenantPort_IsCaseInsensitive()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-a", Port = 5001 },
            ],
        };

        var manager = new TenantManager(settings);

        // Act
        var portLower = manager.GetTenantPort("tenant-a");
        var portUpper = manager.GetTenantPort("TENANT-A");
        var portMixed = manager.GetTenantPort("Tenant-A");

        // Assert
        Assert.Equal(5001, portLower);
        Assert.Equal(5001, portUpper);
        Assert.Equal(5001, portMixed);
    }

    [Fact]
    public void TenantExists_WithValidTenant_ReturnsTrue()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-a", Port = 5001 },
            ],
        };

        var manager = new TenantManager(settings);

        // Act & Assert
        Assert.True(manager.TenantExists("tenant-a"));
    }

    [Fact]
    public void TenantExists_WithInvalidTenant_ReturnsFalse()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-a", Port = 5001 },
            ],
        };

        var manager = new TenantManager(settings);

        // Act & Assert
        Assert.False(manager.TenantExists("invalid-tenant"));
    }

    [Fact]
    public void GetTenants_ReturnsAllTenants()
    {
        // Arrange
        var settings = new MultitenantSettings
        {
            Tenants =
            [
                new TenantConfiguration { TenantId = "tenant-a", Port = 5001 },
                new TenantConfiguration { TenantId = "tenant-b", Port = 5002 },
                new TenantConfiguration { TenantId = "tenant-c", Port = 5003 },
            ],
        };

        var manager = new TenantManager(settings);

        // Act
        var tenants = manager.GetTenants();

        // Assert
        Assert.Equal(3, tenants.Count);
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TenantManager(null!));
    }
}
