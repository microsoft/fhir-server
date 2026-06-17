// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Modules
{
    /// <summary>
    /// Tests that the FhirModule properly registers all expected pipeline behaviors
    /// when using Medino, ensuring all closed generic IPipelineBehavior interfaces are resolvable.
    /// </summary>
    [Trait("OwningTeam", "@microsoft/health/fhir")]
    [Trait("Category", "Unit")]
    public class FhirModuleRegistrationTests
    {
        /// <summary>
        /// Verifies that all closed generic <c>IPipelineBehavior&lt;,&gt;</c> interfaces implemented by
        /// ProvenanceHeaderBehavior are properly registered in the service collection.
        /// </summary>
        [Fact]
        public void ProvenanceHeaderBehavior_AllClosedGenericInterfaces_AreRegistered()
        {
            // Arrange
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            // Act & Assert - verify each closed generic interface is registered as a service descriptor
            var createResourceBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(createResourceBehavior);

            var upsertResourceBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(upsertResourceBehavior);

            var conditionalCreateBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(conditionalCreateBehavior);

            var conditionalUpsertBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(conditionalUpsertBehavior);
        }

        /// <summary>
        /// Verifies that all closed generic <c>IPipelineBehavior&lt;,&gt;</c> interfaces implemented by
        /// ProfileResourcesBehaviour are properly registered in the service collection.
        /// </summary>
        [Fact]
        public void ProfileResourcesBehaviour_AllClosedGenericInterfaces_AreRegistered()
        {
            // Arrange
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            // Act & Assert - verify DeleteResourceResponse is registered to ProfileResourcesBehaviour
            var deleteBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>));
            Assert.NotNull(deleteBehavior);

            // Verify the remaining closed generic interfaces for ProfileResourcesBehaviour are also registered
            var upsertBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(upsertBehavior);

            var conditionalCreateBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(conditionalCreateBehavior);

            var conditionalUpsertBehavior = services.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>));
            Assert.NotNull(conditionalUpsertBehavior);
        }
    }
}
