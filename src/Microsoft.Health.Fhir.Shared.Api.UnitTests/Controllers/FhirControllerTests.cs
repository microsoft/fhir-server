// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Filters.Metrics;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public sealed class FhirControllerTests
    {
        private readonly Type _targetFhirControllerClass = typeof(FhirController);
        private readonly FhirController _fhirController;
        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly IOptions<FeatureConfiguration> _configuration;
        private readonly IAuthorizationService _authorizationService;

        public FhirControllerTests()
        {
            _mediator = Substitute.For<IMediator>();
            _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _urlResolver = Substitute.For<IUrlResolver>();
            _configuration = Substitute.For<IOptions<FeatureConfiguration>>();
            _configuration.Value.Returns(new FeatureConfiguration());
            _authorizationService = Substitute.For<IAuthorizationService>();

            _mediator.SendAsync(
                Arg.Any<DeleteResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new DeleteResourceResponse(new ResourceKey(KnownResourceTypes.Patient, Guid.NewGuid().ToString()))));
            _mediator.SendAsync(
                Arg.Any<ConditionalDeleteResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new DeleteResourceResponse(new ResourceKey(KnownResourceTypes.Patient, Guid.NewGuid().ToString()))));
            _fhirController = new FhirController(
                _mediator,
                _requestContextAccessor,
                _urlResolver,
                _configuration,
                _authorizationService);
            _fhirController.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfAllExpectedServiceFilterAttributesArePresent()
        {
            Type[] expectedCustomAttributes = new Type[]
            {
                typeof(AuditLoggingFilterAttribute),
                typeof(OperationOutcomeExceptionFilterAttribute),
                typeof(ValidateFormatParametersAttribute),
                typeof(QueryLatencyOverEfficiencyFilterAttribute),
            };

            ServiceFilterAttribute[] serviceFilterAttributes = Attribute.GetCustomAttributes(_targetFhirControllerClass, typeof(ServiceFilterAttribute))
                .Select(a => a as ServiceFilterAttribute)
                .ToArray();

            foreach (Type expectedCustomAttribute in expectedCustomAttributes)
            {
                bool attributeWasFound = serviceFilterAttributes.Any(s => s.ServiceType == expectedCustomAttribute);

                if (!attributeWasFound)
                {
                    string errorMessage = $"The custom attribute '{expectedCustomAttribute}' is not assigned to '{nameof(FhirController)}'.";
                    Assert.Fail(errorMessage);
                }
            }
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfTheBundleEndpointHasTheLatencyMetricFilter()
        {
            Type expectedCustomAttribute = typeof(BundleEndpointMetricEmitterAttribute);

            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "BatchAndTransactions", _targetFhirControllerClass);
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfTheSearchEndpointsHaveTheLatencyMetricFilter()
        {
            Type expectedCustomAttribute = typeof(SearchEndpointMetricEmitterAttribute);

            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "History", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Search", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "SearchByResourceType", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "SearchCompartmentByResourceType", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "SystemHistory", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "TypeHistory", _targetFhirControllerClass);
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfTheCrudEndpointsHaveTheLatencyMetricFilter()
        {
            Type expectedCustomAttribute = typeof(CrudEndpointMetricEmitterAttribute);

            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Create", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Delete", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Read", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Update", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "VRead", _targetFhirControllerClass);
        }

        [Theory]
        [InlineData(KnownQueryParameterNames.BulkHardDelete, DeleteOperation.HardDelete)]
        [InlineData(KnownQueryParameterNames.HardDelete, DeleteOperation.HardDelete)]
        [InlineData(null, DeleteOperation.SoftDelete)]
        [InlineData("", DeleteOperation.SoftDelete)]
        [InlineData("xyz", DeleteOperation.SoftDelete)]
        public async Task GivenConditionalDeleteResourceRequest_WhenHardDeleteFlagProvided_HardDeleteShouldBePerformed(string hardDeleteFlag, DeleteOperation operation)
        {
            HardDeleteModel hardDeleteModel = new HardDeleteModel
            {
                BulkHardDelete = (hardDeleteFlag?.Equals(KnownQueryParameterNames.BulkHardDelete) ?? false) ? true : null,
                HardDelete = (hardDeleteFlag?.Equals(KnownQueryParameterNames.HardDelete) ?? false) ? true : null,
            };

            await _fhirController.ConditionalDelete(KnownResourceTypes.Patient, hardDeleteModel, null);
            await _mediator.Received(1).SendAsync(
                Arg.Is<ConditionalDeleteResourceRequest>(x => x.DeleteOperation == operation),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(KnownQueryParameterNames.BulkHardDelete, DeleteOperation.HardDelete)]
        [InlineData(KnownQueryParameterNames.HardDelete, DeleteOperation.HardDelete)]
        [InlineData(null, DeleteOperation.SoftDelete)]
        [InlineData("", DeleteOperation.SoftDelete)]
        [InlineData("xyz", DeleteOperation.SoftDelete)]
        public async Task GivenDeleteResourceRequest_WhenHardDeleteFlagProvided_HardDeleteShouldBePerformed(string hardDeleteKey, DeleteOperation operation)
        {
            HardDeleteModel hardDeleteModel = new HardDeleteModel
            {
                BulkHardDelete = (hardDeleteKey?.Equals(KnownQueryParameterNames.BulkHardDelete) ?? false) ? true : null,
                HardDelete = (hardDeleteKey?.Equals(KnownQueryParameterNames.HardDelete) ?? false) ? true : null,
            };

            await _fhirController.Delete(KnownResourceTypes.Patient, Guid.NewGuid().ToString(), hardDeleteModel, false);
            await _mediator.Received(1).SendAsync(
                Arg.Is<DeleteResourceRequest>(x => x.DeleteOperation == operation),
                Arg.Any<CancellationToken>());
        }

        private static void TestIfTargetMethodContainsCustomAttribute(Type expectedCustomAttributeType, string methodName, Type targetClassType)
        {
            MethodInfo bundleMethodInfo = targetClassType.GetMethod(methodName);
            Assert.True(bundleMethodInfo != null, $"The method '{methodName}' was not found in '{targetClassType.Name}'. Was it renamed or removed?");

            TypeFilterAttribute latencyFilter = Attribute.GetCustomAttributes(bundleMethodInfo, typeof(TypeFilterAttribute))
                .Select(a => a as TypeFilterAttribute)
                .Where(a => a.ImplementationType == expectedCustomAttributeType)
                .SingleOrDefault();

            Assert.True(latencyFilter != null, $"The expected filter '{expectedCustomAttributeType.Name}' was not found in the method '{methodName}' from '{targetClassType.Name}'.");
        }
    }
}
