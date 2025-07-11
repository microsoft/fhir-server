// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.BackgroundJobService;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public sealed class HostingBackgroundServiceTests
{
    [Fact]
    public void GivenOperationsConfigurationWithMixedEnabledQueues_WhenGetEnabledQueueConfigsIsCalled_ThenOnlyEnabledQueuesAreReturned()
    {
        // Arrange
        var exportConfig = new ExportJobConfiguration { Enabled = true };
        var importConfig = new ImportJobConfiguration { Enabled = false };
        var bulkDeleteConfig = new BulkDeleteJobConfiguration { Enabled = true };
        var bulkUpdateConfig = new BulkUpdateJobConfiguration { Enabled = true };

        var operationsConfig = new OperationsConfiguration
        {
            Export = exportConfig,
            Import = importConfig,
            BulkDelete = bulkDeleteConfig,
            BulkUpdate = bulkUpdateConfig,
        };

        var jobHostingFactory = Substitute.For<IScopeProvider<JobHosting>>();
        var hostingConfig = Options.Create(new TaskHostingConfiguration());
        var operationsConfigOptions = Options.Create(operationsConfig);
        var logger = Substitute.For<ILogger<HostingBackgroundService>>();

        var service = new HostingBackgroundService(jobHostingFactory, hostingConfig, operationsConfigOptions, logger);

        // Act
        var enabledQueues = service.GetEnabledQueueConfigs().ToList();

        // Assert
        Assert.Contains(enabledQueues, q => q == exportConfig);
        Assert.Contains(enabledQueues, q => q == bulkDeleteConfig);
        Assert.Contains(enabledQueues, q => q == bulkUpdateConfig);
        Assert.DoesNotContain(enabledQueues, q => q == importConfig);
        Assert.Equal(3, enabledQueues.Count);
    }
}
