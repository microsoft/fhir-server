// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantServiceBlueprintTests
    {
        [Fact]
        public void GivenABlueprint_WhenSnapshotIsTaken_ThenAllRootDescriptorsArePresent()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SampleService>();
            services.AddScoped<OtherService>();

            ITenantServiceBlueprint blueprint = new TenantServiceBlueprint(services);

            IReadOnlyList<ServiceDescriptor> snapshot = blueprint.CreateSnapshot();

            Assert.Contains(snapshot, d => d.ServiceType == typeof(SampleService));
            Assert.Contains(snapshot, d => d.ServiceType == typeof(OtherService));
        }

        [Fact]
        public void GivenABlueprint_WhenTheRootCollectionIsBuilt_ThenTheSnapshotIsStillReadable()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SampleService>();

            ITenantServiceBlueprint blueprint = new TenantServiceBlueprint(services);

            using ServiceProvider root = services.BuildServiceProvider();

            IReadOnlyList<ServiceDescriptor> snapshot = blueprint.CreateSnapshot();

            Assert.Contains(snapshot, d => d.ServiceType == typeof(SampleService));
        }

        [Fact]
        public void GivenABlueprint_WhenTheRootCollectionChangesBetweenSnapshots_ThenEachSnapshotPreservesItsCaptureTime()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SampleService>();

            ITenantServiceBlueprint blueprint = new TenantServiceBlueprint(services);

            IReadOnlyList<ServiceDescriptor> first = blueprint.CreateSnapshot();
            services.AddSingleton<OtherService>();
            IReadOnlyList<ServiceDescriptor> second = blueprint.CreateSnapshot();

            Assert.Single(first);
            Assert.DoesNotContain(first, d => d.ServiceType == typeof(OtherService));
            Assert.Equal(2, second.Count);
            Assert.Contains(second, d => d.ServiceType == typeof(OtherService));
        }

        [Fact]
        public void GivenABlueprint_WhenTheRootCollectionGrowsAfterCapture_ThenTheSnapshotIncludesTheAddition()
        {
            var services = new ServiceCollection();
            services.AddSingleton<SampleService>();

            ITenantServiceBlueprint blueprint = new TenantServiceBlueprint(services);

            services.AddSingleton<OtherService>();

            Assert.Contains(blueprint.CreateSnapshot(), d => d.ServiceType == typeof(OtherService));
        }

        private sealed class SampleService
        {
        }

        private sealed class OtherService
        {
        }
    }
}
