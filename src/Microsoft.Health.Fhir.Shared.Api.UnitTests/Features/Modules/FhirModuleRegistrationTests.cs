// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Reflection;
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
            AssertPipelineBehaviorRegistered<CreateResourceRequest, UpsertResourceResponse, ProvenanceHeaderPipelineBehavior<CreateResourceRequest>>(services);
            AssertPipelineBehaviorRegistered<UpsertResourceRequest, UpsertResourceResponse, ProvenanceHeaderPipelineBehavior<UpsertResourceRequest>>(services);
            AssertPipelineBehaviorRegistered<ConditionalCreateResourceRequest, UpsertResourceResponse, ProvenanceHeaderPipelineBehavior<ConditionalCreateResourceRequest>>(services);
            AssertPipelineBehaviorRegistered<ConditionalUpsertResourceRequest, UpsertResourceResponse, ProvenanceHeaderPipelineBehavior<ConditionalUpsertResourceRequest>>(services);
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
            AssertPipelineBehaviorRegistered<DeleteResourceRequest, DeleteResourceResponse, ProfileResourcesPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>>(services);

            // Verify the remaining closed generic interfaces for ProfileResourcesBehaviour are also registered
            AssertPipelineBehaviorRegistered<CreateResourceRequest, UpsertResourceResponse, ProfileResourcesPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>>(services);
            AssertPipelineBehaviorRegistered<UpsertResourceRequest, UpsertResourceResponse, ProfileResourcesPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>>(services);
            AssertPipelineBehaviorRegistered<ConditionalCreateResourceRequest, UpsertResourceResponse, ProfileResourcesPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>>(services);
            AssertPipelineBehaviorRegistered<ConditionalUpsertResourceRequest, UpsertResourceResponse, ProfileResourcesPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>>(services);
        }

        [Fact]
        public void RegisteredPipelineBehaviors_WhenMedinoLocatesHandleAsync_ThenHandleMethodsAreUnambiguous()
        {
            var services = CreateServiceCollection();
            var closedPipelineBehaviorDescriptors = services
                .Where(serviceDescriptor =>
                    serviceDescriptor.ServiceType.IsGenericType &&
                    serviceDescriptor.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>) &&
                    !serviceDescriptor.ServiceType.ContainsGenericParameters)
                .ToList();

            Assert.NotEmpty(closedPipelineBehaviorDescriptors);

            foreach (var descriptor in closedPipelineBehaviorDescriptors)
            {
                var implementationType = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

                Assert.NotNull(implementationType);

                MethodInfo handleAsyncMethod = null;

                var exception = Record.Exception(() => handleAsyncMethod = implementationType.GetMethod("HandleAsync"));

                Assert.Null(exception);
                Assert.NotNull(handleAsyncMethod);
            }
        }

        private static IServiceCollection CreateServiceCollection()
        {
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            return services;
        }

        private static void AssertPipelineBehaviorRegistered<TRequest, TResponse, TImplementation>(IServiceCollection services)
        {
            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IPipelineBehavior<TRequest, TResponse>) &&
                    descriptor.ImplementationType == typeof(TImplementation));
        }
    }
}
