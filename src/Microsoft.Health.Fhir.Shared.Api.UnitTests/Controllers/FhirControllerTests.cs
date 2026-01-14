// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Filters.Metrics;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

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
            _urlResolver.ResolveResourceUrl(
                Arg.Any<IResourceElement>(),
                Arg.Any<bool>())
                .Returns(new Uri("https://localhost/location"));
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

        [Theory]
        [InlineData("p=v", null, SaveOutcomeType.Created)]
        [InlineData("p=v", null, null)]
        [InlineData("p=v", ConditionalQueryProcessingLogic.Sequential, SaveOutcomeType.MatchFound)]
        [InlineData("p=v", ConditionalQueryProcessingLogic.Parallel, SaveOutcomeType.Created)]
        [InlineData("p0=v0&p1=v1&p2=v2", ConditionalQueryProcessingLogic.Parallel, SaveOutcomeType.Created)]
        public async Task GivenConditionalCreateRequest_WhenVariousHeadersAreSpecified_ThenRequestShouldBeHandledCorrectly(
            string ifNotExist,
            ConditionalQueryProcessingLogic? queryProcessingLogic,
            SaveOutcomeType? saveOutcome)
        {
            var resource = new Patient()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var wrapper = new ResourceWrapper(
                resource.ToResourceElement(),
                new RawResource(resource.ToJson(), FhirResourceFormat.Json, false),
                null,
                false,
                null,
                null,
                null);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[KnownHeaders.IfNoneExist] = ifNotExist;
            if (queryProcessingLogic.HasValue)
            {
                httpContext.Request.Headers[KnownHeaders.ConditionalQueryProcessingLogic] = queryProcessingLogic.Value.ToString();
            }

            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<ConditionalCreateResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(saveOutcome == null ? null : new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), saveOutcome.Value)));

            var request = default(ConditionalCreateResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<ConditionalCreateResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<ConditionalCreateResourceRequest>());

            var response = await _fhirController.ConditionalCreate(resource);
            if (saveOutcome.HasValue)
            {
                Assert.IsType<FhirResult>(response);
                Assert.Equal(
                    saveOutcome.Value == SaveOutcomeType.Created ? HttpStatusCode.Created : HttpStatusCode.OK,
                    ((FhirResult)response).StatusCode);
            }
            else
            {
                Assert.IsType<OkResult>(response);
            }

            var expectedHeaders = QueryHelpers.ParseQuery(ifNotExist);
            Assert.Equal(expectedHeaders.Count, request?.ConditionalParameters.Count ?? 0);
            Assert.All(
                expectedHeaders,
                x =>
                {
                    Assert.Contains(
                        request?.ConditionalParameters,
                        y =>
                        {
                            return string.Equals(x.Key, y.Item1, StringComparison.Ordinal)
                                && string.Equals(x.Value.ToString(), y.Item2, StringComparison.Ordinal);
                        });
                });

            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<ConditionalCreateResourceRequest>(),
                Arg.Any<CancellationToken>());
            _requestContextAccessor.RequestContext.Properties
                .Received(queryProcessingLogic.HasValue && queryProcessingLogic.Value == ConditionalQueryProcessingLogic.Parallel ? 1 : 0)
                .TryAdd(KnownQueryParameterNames.OptimizeConcurrency, true);
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
