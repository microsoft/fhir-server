// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class MediationModuleTests
    {
        [Theory]
        [InlineData(typeof(ValidateRequestPreProcessor<,>))]
        [InlineData(typeof(ValidateCapabilityPreProcessor<,>))]
        public void GivenMediationModule_WhenLoaded_ThenRegistersGenericPipelineBehavior(Type implementationType)
        {
            var services = new ServiceCollection();

            new MediationModule().Load(services);

            Assert.Contains(
                services,
                service => service.ServiceType == typeof(IPipelineBehavior<,>) &&
                           service.ImplementationType == implementationType);
        }

        [Fact]
        public void GivenMediationModule_WhenLoaded_ThenRegistersValidationPipelineBehaviorsInExpectedOrder()
        {
            var services = new ServiceCollection();

            new MediationModule().Load(services);

            var validationBehaviors = services
                .Where(service => service.ServiceType == typeof(IPipelineBehavior<,>) ||
                                  service.ServiceType == typeof(IPipelineBehavior<Core.Messages.Bundle.BundleRequest, Core.Messages.Bundle.BundleResponse>))
                .Select(service => service.ImplementationType)
                .Where(type => type == typeof(ValidateRequestPreProcessor<,>) ||
                               type == typeof(ValidateBundlePreProcessor) ||
                               type == typeof(ValidateCapabilityPreProcessor<,>))
                .ToArray();

            Assert.Equal(
                new[]
                {
                    typeof(ValidateRequestPreProcessor<,>),
                    typeof(ValidateBundlePreProcessor),
                    typeof(ValidateCapabilityPreProcessor<,>),
                },
                validationBehaviors);
        }

        [Theory]
        [InlineData(typeof(ProvenanceHeaderBehavior), true)]
        [InlineData(typeof(ProfileResourcesBehaviour), true)]
        [InlineData(typeof(ProvenanceHeaderBehavior), false)]
        [InlineData(typeof(ProfileResourcesBehaviour), false)]
        public void GivenMediationAndFhirModules_WhenLoadedInEitherOrder_ThenManualPipelineBehaviorsAreRegisteredOnce(
            Type implementationType,
            bool loadMediationModuleFirst)
        {
            var services = new ServiceCollection();

            if (loadMediationModuleFirst)
            {
                new MediationModule().Load(services);
                new FhirModule().Load(services);
            }
            else
            {
                new FhirModule().Load(services);
                new MediationModule().Load(services);
            }

            foreach (Type serviceType in implementationType.GetInterfaces().Where(IsPipelineBehavior))
            {
                var scannedRegistrations = services
                    .Where(service => service.ServiceType == serviceType && service.ImplementationType == implementationType)
                    .ToArray();

                Assert.Empty(scannedRegistrations);
                Assert.Contains(services, service => service.ServiceType == serviceType && service.ImplementationFactory != null);
            }
        }

        private static bool IsPipelineBehavior(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
        }
    }
}
