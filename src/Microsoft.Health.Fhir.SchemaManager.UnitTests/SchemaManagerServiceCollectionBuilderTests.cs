// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema.Messages.Notifications;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Schema)]
public class SchemaManagerServiceCollectionBuilderTests
{
    [Fact]
    public void GivenSchemaManagerServices_WhenRegistered_ThenRegistersMediatorWithoutFhirSchemaUpgradedHandler()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddSchemaManager(config);

        Assert.Contains(services, service => service.ServiceType == typeof(IMediator));
        Assert.DoesNotContain(
            services,
            service => service.ServiceType == typeof(INotificationHandler<SchemaUpgradedNotification>) &&
                       service.ImplementationType == typeof(SchemaUpgradedHandler));
    }
}
