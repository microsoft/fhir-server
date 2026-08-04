// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantHostedServicePolicyTests
    {
        [Theory]
        [InlineData("Microsoft.Health.Fhir.Api.Features.BackgroundJobService.HostingBackgroundService", TenantHostedServiceDisposition.Relocated)]
        [InlineData("Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.WatchdogsBackgroundService", TenantHostedServiceDisposition.Relocated)]
        [InlineData("Microsoft.Health.Fhir.Core.Features.Search.Registry.SearchParameterCacheRefreshBackgroundService", TenantHostedServiceDisposition.Relocated)]
        [InlineData("Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanReuseChecker", TenantHostedServiceDisposition.Relocated)]
        [InlineData("Microsoft.Health.Fhir.CosmosDb.Features.Storage.CosmosContainerProvider", TenantHostedServiceDisposition.Relocated)]
        [InlineData("Microsoft.Health.Fhir.Core.Features.Definition.CompartmentDefinitionManager", TenantHostedServiceDisposition.Shared)]
        [InlineData("Microsoft.Health.Fhir.Core.Features.Search.CodeSystemResolver", TenantHostedServiceDisposition.Shared)]
        [InlineData("Microsoft.Health.Fhir.Web.PrometheusMetricsServer", TenantHostedServiceDisposition.Shared)]
        [InlineData("Microsoft.Health.Fhir.Api.OpenIddict.Services.OpenIddictApplicationCreater", TenantHostedServiceDisposition.Shared)]
        [InlineData("Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckPublisherHostedService", TenantHostedServiceDisposition.Shared)]
        [InlineData("Microsoft.Health.Api.Features.Audit.AuditEventTypeMapping", TenantHostedServiceDisposition.Shared)]
        [InlineData("Microsoft.Health.Fhir.Core.Features.Security.RoleLoader", TenantHostedServiceDisposition.PerTenantInitializer)]
        public void GivenTheDefaultPolicy_WhenAKnownServiceIsClassified_ThenTheExpectedDispositionIsReturned(
            string typeName,
            TenantHostedServiceDisposition expected)
        {
            var policy = new TenantHostedServicePolicy();

            Assert.Equal(expected, policy.Classify(typeName));
        }

        [Fact]
        public void GivenTheDefaultPolicy_WhenAnUnknownServiceIsClassified_ThenAnExceptionNamingTheTypeIsThrown()
        {
            var policy = new TenantHostedServicePolicy();

            TenantHostedServiceNotClassifiedException exception =
                Assert.Throws<TenantHostedServiceNotClassifiedException>(
                    () => policy.Classify("Contoso.Fhir.SomeNewBackgroundService"));

            Assert.Contains("Contoso.Fhir.SomeNewBackgroundService", exception.Message, StringComparison.Ordinal);
            Assert.Equal("Contoso.Fhir.SomeNewBackgroundService", exception.HostedServiceTypeName);
        }

        [Fact]
        public void GivenACustomClassification_WhenClassified_ThenTheCustomValueWins()
        {
            var policy = new TenantHostedServicePolicy();
            policy.Set("Contoso.Fhir.SomeNewBackgroundService", TenantHostedServiceDisposition.PerTenantInitializer);

            Assert.Equal(
                TenantHostedServiceDisposition.PerTenantInitializer,
                policy.Classify("Contoso.Fhir.SomeNewBackgroundService"));
        }

        [Fact]
        public void GivenAnOverriddenDefault_WhenClassified_ThenTheOverrideWins()
        {
            var policy = new TenantHostedServicePolicy();
            policy.Set(
                "Microsoft.Health.Fhir.Core.Features.Security.RoleLoader",
                TenantHostedServiceDisposition.Shared);

            Assert.Equal(
                TenantHostedServiceDisposition.Shared,
                policy.Classify("Microsoft.Health.Fhir.Core.Features.Security.RoleLoader"));
        }
    }
}
