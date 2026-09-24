// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Medino;
using Medino.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Search.Behavior;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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

        [Fact]
        public void GivenMediationModule_WhenLoaded_ThenListSearchPipeBehaviorIsNotRegistered()
        {
            var services = new ServiceCollection();

            new MediationModule().Load(services);

            var listSearchBehaviors = services
                .Where(service => service.ServiceType == typeof(IPipelineBehavior<SearchResourceRequest, SearchResourceResponse>))
                .Select(service => service.ImplementationType)
                .Where(type => type == typeof(ListSearchPipeBehavior))
                .ToArray();

            Assert.Empty(listSearchBehaviors);
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

        [Fact]
        public async Task GivenOpenGenericCapabilityBehavior_WhenRequestIsSent_ThenCapabilityIsValidatedOnce()
        {
            var services = new ServiceCollection();
            var conformanceProvider = Substitute.For<IConformanceProvider>();
            conformanceProvider.SatisfiesAsync(
                Arg.Any<IReadOnlyCollection<CapabilityQuery>>(),
                Arg.Any<CancellationToken>())
                .Returns(true);
            services.AddMedino(typeof(MediationModuleTests).Assembly);
            services.AddSingleton(conformanceProvider);
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateCapabilityPreProcessor<,>));

            await using ServiceProvider provider = services.BuildServiceProvider();
            IMediator mediator = provider.GetRequiredService<IMediator>();

            await mediator.SendAsync(new ValidationPipelineTestRequest());

            await conformanceProvider.Received(1).SatisfiesAsync(
                Arg.Any<IReadOnlyCollection<CapabilityQuery>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOpenGenericRequestBehavior_WhenRequestIsSent_ThenOnlyTypedValidatorRuns()
        {
            var services = new ServiceCollection();
            var typedValidator = new CountingValidator<ValidationPipelineTestRequest>();
            var objectValidator = new CountingValidator<object>();
            services.AddMedino(typeof(MediationModuleTests).Assembly);
            services.AddSingleton<IValidator<ValidationPipelineTestRequest>>(typedValidator);
            services.AddSingleton<IValidator<object>>(objectValidator);
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateRequestPreProcessor<,>));

            await using ServiceProvider provider = services.BuildServiceProvider();
            IMediator mediator = provider.GetRequiredService<IMediator>();

            await mediator.SendAsync(new ValidationPipelineTestRequest());

            Assert.Equal(1, typedValidator.ExecutionCount);
            Assert.Equal(0, objectValidator.ExecutionCount);
        }

        private static bool IsPipelineBehavior(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
        }

        public sealed class ValidationPipelineTestRequest : IRequest<ValidationPipelineTestResponse>, IRequireCapability
        {
            public IEnumerable<CapabilityQuery> RequiredCapabilities()
            {
                yield return new CapabilityQuery("true");
            }
        }

        public sealed class ValidationPipelineTestResponse
        {
        }

        public sealed class ValidationPipelineTestHandler : IRequestHandler<ValidationPipelineTestRequest, ValidationPipelineTestResponse>
        {
            public Task<ValidationPipelineTestResponse> HandleAsync(
                ValidationPipelineTestRequest request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new ValidationPipelineTestResponse());
            }
        }

        public sealed class CountingValidator<T> : AbstractValidator<T>
        {
            public CountingValidator()
            {
                RuleFor(x => x).Custom((_, _) => ExecutionCount++);
            }

            public int ExecutionCount { get; private set; }
        }
    }
}
