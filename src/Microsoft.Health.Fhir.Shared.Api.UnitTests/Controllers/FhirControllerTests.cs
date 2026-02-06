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
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
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

            _mediator.Send(
                Arg.Any<DeleteResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new DeleteResourceResponse(new ResourceKey(KnownResourceTypes.Patient, Guid.NewGuid().ToString()))));
            _mediator.Send(
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
            await _mediator.Received(1).Send(
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
            await _mediator.Received(1).Send(
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

            var response = await _fhirController.ConditionalCreate(resource.ToResourceElement());
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

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.MethodNotAllowed)]
        [InlineData(null)]
        public void GivenHttpStatusCode_WhenProcessingCustomError_ThenCorrectFhirResultShouldBeCreated(
            HttpStatusCode? statusCode)
        {
            var expectedStatusCode = statusCode ?? HttpStatusCode.InternalServerError;
            var issueTypeMap = new Dictionary<HttpStatusCode, OperationOutcome.IssueType>
            {
                [HttpStatusCode.Unauthorized] = OperationOutcome.IssueType.Login,
                [HttpStatusCode.Forbidden] = OperationOutcome.IssueType.Forbidden,
                [HttpStatusCode.NotFound] = OperationOutcome.IssueType.NotFound,
                [HttpStatusCode.MethodNotAllowed] = OperationOutcome.IssueType.NotSupported,
                [HttpStatusCode.InternalServerError] = OperationOutcome.IssueType.Exception,
            };

            var diagnosticMap = new Dictionary<HttpStatusCode, string>
            {
                [HttpStatusCode.Unauthorized] = Resources.Unauthorized,
                [HttpStatusCode.Forbidden] = Resources.Forbidden,
                [HttpStatusCode.NotFound] = Resources.NotFoundException,
                [HttpStatusCode.MethodNotAllowed] = Resources.OperationNotSupported,
                [HttpStatusCode.InternalServerError] = Resources.GeneralInternalError,
            };

            var response = _fhirController.CustomError(statusCode != null ? (int)statusCode.Value : null);
            var result = response as FhirResult;
            Assert.NotNull(result);
            Assert.Equal(expectedStatusCode, result.StatusCode);

            var outcome = (result.Result as ResourceElement)?.ToPoco<OperationOutcome>();
            Assert.NotNull(outcome);
            Assert.Single(outcome.Issue);

            var issue = outcome.Issue.First();
            Assert.Equal(OperationOutcome.IssueSeverity.Error, issue.Severity);
            Assert.Equal(issueTypeMap[expectedStatusCode], issue.Code);
            Assert.Equal(diagnosticMap[expectedStatusCode], issue.Diagnostics);
        }

        [Fact]
        public async Task GivenCreateRequest_WhenProcessingRequest_ThenCreateResourceRequestShouldBeCreatedCorrectly()
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
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created)));

            var request = default(CreateResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<CreateResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<CreateResourceRequest>());

            var response = await _fhirController.Create(resource.ToResourceElement());
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.Resource);
            Assert.Equal(resource.TypeName, request.Resource.InstanceType);
            Assert.Equal(resource.Id, request.Resource.Id);
            Assert.Equal(resource.VersionId, request.Resource.VersionId);

            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("ver0", false)]
        [InlineData("ver1", true)]
        public async Task GivenUpdateRequest_WhenProcessingRequest_ThenUpsertResourceRequestShouldBeCreatedCorrectly(
            string versionId,
            bool metaHistory)
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
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<UpsertResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Updated)));

            var request = default(UpsertResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<UpsertResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<UpsertResourceRequest>());

            var response = await _fhirController.Update(
                resource.ToResourceElement(),
                versionId != null ? WeakETag.FromVersionId(versionId) : null,
                metaHistory);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.Resource);
            Assert.Equal(resource.TypeName, request.Resource.InstanceType);
            Assert.Equal(resource.Id, request.Resource.Id);
            Assert.Equal(resource.VersionId, request.Resource.VersionId);
            Assert.Equal(versionId, request.WeakETag?.VersionId);
            Assert.Equal(metaHistory, request.MetaHistory);

            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<UpsertResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("?p=v", null, SaveOutcomeType.Updated)]
        [InlineData("?p=v", null, SaveOutcomeType.Created)]
        [InlineData("?p=v", null, SaveOutcomeType.MatchFound)]
        [InlineData(null, null, SaveOutcomeType.Updated)]
        [InlineData("?p=v", ConditionalQueryProcessingLogic.Sequential, SaveOutcomeType.Created)]
        [InlineData("?p=v", ConditionalQueryProcessingLogic.Parallel, SaveOutcomeType.Updated)]
        [InlineData("?p0=v0&p1=v1&p2=v2", ConditionalQueryProcessingLogic.Parallel, SaveOutcomeType.Updated)]
        public async Task GivenConditionalUpdateRequest_WhenVariousHeadersAreSpecified_ThenRequestShouldBeHandledCorrectly(
            string query,
            ConditionalQueryProcessingLogic? queryProcessingLogic,
            SaveOutcomeType saveOutcome)
        {
            var statusCodeMap = new Dictionary<SaveOutcomeType, HttpStatusCode>
            {
                [SaveOutcomeType.Created] = HttpStatusCode.Created,
                [SaveOutcomeType.Updated] = HttpStatusCode.OK,
                [SaveOutcomeType.MatchFound] = HttpStatusCode.BadRequest,
            };

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
            httpContext.Request.QueryString = new QueryString(query);
            if (queryProcessingLogic.HasValue)
            {
                httpContext.Request.Headers[KnownHeaders.ConditionalQueryProcessingLogic] = queryProcessingLogic.Value.ToString();
            }

            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<ConditionalUpsertResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), saveOutcome)));

            var request = default(ConditionalUpsertResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<ConditionalUpsertResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<ConditionalUpsertResourceRequest>());

            var response = await _fhirController.ConditionalUpdate(resource.ToResourceElement());
            Assert.IsType<FhirResult>(response);
            Assert.Equal(statusCodeMap[saveOutcome], ((FhirResult)response).StatusCode);

            var headers = QueryHelpers.ParseQuery(query);
            Assert.Equal(headers.Count, request?.ConditionalParameters.Count ?? 0);
            Assert.All(
                headers,
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
                Arg.Any<ConditionalUpsertResourceRequest>(),
                Arg.Any<CancellationToken>());
            _requestContextAccessor.RequestContext.Properties
                .Received(queryProcessingLogic.HasValue && queryProcessingLogic.Value == ConditionalQueryProcessingLogic.Parallel ? 1 : 0)
                .TryAdd(KnownQueryParameterNames.OptimizeConcurrency, true);
        }

        [Fact]
        public async Task GivenReadRequest_WhenProcessingRequest_ThenGetResourceRequestShouldBeCreatedCorrectly()
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
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetResourceResponse(new RawResourceElement(wrapper)));

            var request = default(GetResourceRequest);
            _mediator.When(
                x => x.Send<GetResourceResponse>(
                    Arg.Any<GetResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<GetResourceRequest>());

            var response = await _fhirController.Read(resource.TypeName, resource.Id);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.ResourceKey);
            Assert.Equal(resource.TypeName, request.ResourceKey.ResourceType);
            Assert.Equal(resource.Id, request.ResourceKey.Id);

            // NOTE: commenting out version check as Read ignores version id.
            Assert.Null(request.ResourceKey.VersionId);

            await _mediator.Received(1).Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenSystemHistoryRequest_WhenProcessingRequest_ThenGetResourceRequestShouldBeCreatedCorrectly()
        {
            await RunHistoryTest((model, _, _) => _fhirController.SystemHistory(model));
        }

        [Fact]
        public async Task GivenTypeHistoryRequest_WhenProcessingRequest_ThenGetResourceRequestShouldBeCreatedCorrectly()
        {
            await RunHistoryTest(
                (model, type, _) => _fhirController.TypeHistory(type, model),
                Guid.NewGuid().ToString());
        }

        [Fact]
        public async Task GivenHistoryRequest_WhenProcessingRequest_ThenGetResourceRequestShouldBeCreatedCorrectly()
        {
            await RunHistoryTest(
                (model, type, id) => _fhirController.History(type, id, model),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString());
        }

        [Fact]
        public async Task GivenVReadRequest_WhenProcessingRequest_ThenGetResourceRequestShouldBeCreatedCorrectly()
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
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetResourceResponse(new RawResourceElement(wrapper)));

            var request = default(GetResourceRequest);
            _mediator.When(
                x => x.Send<GetResourceResponse>(
                    Arg.Any<GetResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<GetResourceRequest>());

            var response = await _fhirController.VRead(
                resource.TypeName,
                resource.Id,
                resource.VersionId);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.ResourceKey);
            Assert.Equal(resource.TypeName, request.ResourceKey.ResourceType);
            Assert.Equal(resource.Id, request.ResourceKey.Id);
            Assert.Equal(resource.VersionId, request.ResourceKey.VersionId);

            await _mediator.Received(1).Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenPurgeHistoryRequest_WhenProcessingRequest_ThenDeleteResourceRequestShouldBeCreatedCorrectly()
        {
            var resourceKey = new ResourceKey(
                KnownResourceTypes.Patient,
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString());
            var etag = WeakETag.FromVersionId("ver0");
            var httpContext = new DefaultHttpContext();
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<DeleteResourceResponse>(
                Arg.Any<DeleteResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new DeleteResourceResponse(resourceKey, 1, etag));

            var request = default(DeleteResourceRequest);
            _mediator.When(
                x => x.Send<DeleteResourceResponse>(
                    Arg.Any<DeleteResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<DeleteResourceRequest>());

            var response = await _fhirController.PurgeHistory(
                resourceKey.ResourceType,
                resourceKey.Id,
                true);
            var result = response as FhirResult;
            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
            Assert.Null(result.Result);
            Assert.Contains(
                result.Headers,
                x =>
                {
                    return string.Equals(x.Key, Net.Http.Headers.HeaderNames.ETag, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Value, etag.ToString(), StringComparison.Ordinal);
                });

            Assert.NotNull(request?.ResourceKey);
            Assert.Equal(resourceKey.ResourceType, request.ResourceKey.ResourceType);
            Assert.Equal(resourceKey.Id, request.ResourceKey.Id);
            Assert.Equal(DeleteOperation.PurgeHistory, request.DeleteOperation);
            Assert.True(request.AllowPartialSuccess);

            // NOTE: commenting out version check as PurgeHistory ignores version id.
            Assert.Null(request.ResourceKey.VersionId);

            await _mediator.Received(1).Send<DeleteResourceResponse>(
                Arg.Any<DeleteResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("ver0", false)]
        [InlineData("ver1", true)]
        public async Task GivenPatchJsonRequest_WhenProcessingRequest_ThenPatchResourceRequestShouldBeCreatedCorrectly(
            string versionId,
            bool metaHistory)
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
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<PatchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Updated)));

            var request = default(PatchResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<PatchResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<PatchResourceRequest>());

            var response = await _fhirController.PatchJson(
                new AspNetCore.JsonPatch.JsonPatchDocument(),
                resource.TypeName,
                resource.Id,
                versionId != null ? WeakETag.FromVersionId(versionId) : null,
                metaHistory);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.ResourceKey);
            Assert.Equal(resource.TypeName, request.ResourceKey.ResourceType);
            Assert.Equal(resource.Id, request.ResourceKey.Id);
            Assert.Equal(versionId, request.WeakETag?.VersionId);
            Assert.Equal(metaHistory, request.MetaHistory);
            Assert.NotNull(request.Payload);

            // NOTE: commenting out version check as Patch ignores version id.
            Assert.Null(request.ResourceKey.VersionId);

            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<PatchResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null, null, null, false)]
        [InlineData("?p0=v0", ConditionalQueryProcessingLogic.Sequential, null, true)]
        [InlineData("?p0=v0&p1=v1&p2=v2", ConditionalQueryProcessingLogic.Parallel, "ver0", false)]
        public async Task GivenConditionalPatchJsonRequest_WhenProcessingRequest_ThenConditionalPatchResourceRequestShouldBeCreatedCorrectly(
            string query,
            ConditionalQueryProcessingLogic? queryProcessingLogic,
            string versionId,
            bool metaHistory)
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
            httpContext.Request.QueryString = new QueryString(query);
            if (queryProcessingLogic.HasValue)
            {
                httpContext.Request.Headers[KnownHeaders.ConditionalQueryProcessingLogic] = queryProcessingLogic.Value.ToString();
            }

            _fhirController.ControllerContext.HttpContext = httpContext;

            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<ConditionalPatchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Updated)));

            var request = default(ConditionalPatchResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<ConditionalPatchResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<ConditionalPatchResourceRequest>());

            var response = await _fhirController.ConditionalPatchJson(
                resource.TypeName,
                new AspNetCore.JsonPatch.JsonPatchDocument(),
                versionId != null ? WeakETag.FromVersionId(versionId) : null,
                metaHistory);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request);
            Assert.Equal(resource.TypeName, request.ResourceType);
            Assert.Equal(versionId, request.WeakETag?.VersionId);
            Assert.Equal(metaHistory, request.MetaHistory);
            Assert.NotNull(request.Payload);

            var headers = QueryHelpers.ParseQuery(query);
            Assert.Equal(headers.Count, request.ConditionalParameters?.Count ?? 0);
            Assert.All(
                headers,
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
                Arg.Any<ConditionalPatchResourceRequest>(),
                Arg.Any<CancellationToken>());
            _requestContextAccessor.RequestContext.Properties
                .Received(queryProcessingLogic.HasValue && queryProcessingLogic.Value == ConditionalQueryProcessingLogic.Parallel ? 1 : 0)
                .TryAdd(KnownQueryParameterNames.OptimizeConcurrency, true);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("ver0", false)]
        [InlineData("ver1", true)]
        public async Task GivenPatchFhirRequest_WhenProcessingRequest_ThenPatchResourceRequestShouldBeCreatedCorrectly(
            string versionId,
            bool metaHistory)
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
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<PatchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Updated)));

            var request = default(PatchResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<PatchResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<PatchResourceRequest>());

            var response = await _fhirController.PatchFhir(
                new Parameters(),
                resource.TypeName,
                resource.Id,
                versionId != null ? WeakETag.FromVersionId(versionId) : null,
                metaHistory);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.ResourceKey);
            Assert.Equal(resource.TypeName, request.ResourceKey.ResourceType);
            Assert.Equal(resource.Id, request.ResourceKey.Id);
            Assert.Equal(versionId, request.WeakETag?.VersionId);
            Assert.Equal(metaHistory, request.MetaHistory);
            Assert.NotNull(request.Payload);

            // NOTE: commenting out version check as Patch ignores version id.
            Assert.Null(request.ResourceKey.VersionId);

            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<PatchResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null, null, null, false)]
        [InlineData("?p0=v0", ConditionalQueryProcessingLogic.Sequential, null, true)]
        [InlineData("?p0=v0&p1=v1&p2=v2", ConditionalQueryProcessingLogic.Parallel, "ver0", false)]
        public async Task GivenConditionalPatchFhirRequest_WhenProcessingRequest_ThenConditionalPatchResourceRequestShouldBeCreatedCorrectly(
            string query,
            ConditionalQueryProcessingLogic? queryProcessingLogic,
            string versionId,
            bool metaHistory)
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
            httpContext.Request.QueryString = new QueryString(query);
            if (queryProcessingLogic.HasValue)
            {
                httpContext.Request.Headers[KnownHeaders.ConditionalQueryProcessingLogic] = queryProcessingLogic.Value.ToString();
            }

            _fhirController.ControllerContext.HttpContext = httpContext;

            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<ConditionalPatchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Updated)));

            var request = default(ConditionalPatchResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<ConditionalPatchResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<ConditionalPatchResourceRequest>());

            var response = await _fhirController.ConditionalPatchFhir(
                resource.TypeName,
                new Parameters(),
                versionId != null ? WeakETag.FromVersionId(versionId) : null,
                metaHistory);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request);
            Assert.Equal(resource.TypeName, request.ResourceType);
            Assert.Equal(versionId, request.WeakETag?.VersionId);
            Assert.Equal(metaHistory, request.MetaHistory);
            Assert.NotNull(request.Payload);

            var headers = QueryHelpers.ParseQuery(query);
            Assert.Equal(headers.Count, request.ConditionalParameters?.Count ?? 0);
            Assert.All(
                headers,
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
                Arg.Any<ConditionalPatchResourceRequest>(),
                Arg.Any<CancellationToken>());
            _requestContextAccessor.RequestContext.Properties
                .Received(queryProcessingLogic.HasValue && queryProcessingLogic.Value == ConditionalQueryProcessingLogic.Parallel ? 1 : 0)
                .TryAdd(KnownQueryParameterNames.OptimizeConcurrency, true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("?p0=v0")]
        [InlineData("?p0=v0&p1=v1&p2=v2")]
        public async Task GivenSearchRequest_WhenProcessingRequest_ThenSearchResourceRequestShouldBeCreatedCorrectly(
            string query)
        {
            await RunSearchTest(
                (_) => _fhirController.Search(),
                null,
                query);
        }

        [Theory]
        [InlineData(KnownResourceTypes.Practitioner, null)]
        [InlineData(KnownResourceTypes.Patient, "?p0=v0")]
        [InlineData(KnownResourceTypes.Observation, "?p0=v0&p1=v1&p2=v2")]
        public async Task GivenSearchByResourceTypeRequest_WhenProcessingRequest_ThenSearchResourceRequestShouldBeCreatedCorrectly(
            string resourceType,
            string query)
        {
            await RunSearchTest(
                (type) => _fhirController.SearchByResourceType(type),
                resourceType,
                query);
        }

        [Theory]
        [InlineData(KnownResourceTypes.Practitioner, "id0", KnownResourceTypes.Patient, null)]
        [InlineData(KnownResourceTypes.Patient, "id1", KnownResourceTypes.Observation, "?p0=v0")]
        [InlineData(KnownResourceTypes.MedicationRequest, "id2", "*", "?p0=v0&p1=v1&p2=v2")]
        public async Task GivenSearchCompartmentByResourceTypeRequest_WhenProcessingRequest_ThenSearchCompartmentResourceRequestShouldBeCreatedCorrectly(
            string compartmentType,
            string compartmentId,
            string resourceType,
            string query)
        {
            var resource = new Bundle()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString(query);
            _fhirController.ControllerContext.HttpContext = httpContext;

            _mediator.Send<SearchCompartmentResponse>(
                Arg.Any<SearchCompartmentRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchCompartmentResponse(resource.ToResourceElement()));

            var request = default(SearchCompartmentRequest);
            _mediator.When(
                x => x.Send<SearchCompartmentResponse>(
                    Arg.Any<SearchCompartmentRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<SearchCompartmentRequest>());

            var response = await _fhirController.SearchCompartmentByResourceType(
                compartmentType,
                compartmentId,
                resourceType);
            var result = response as FhirResult;
            Assert.NotNull(result?.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request);
            Assert.Equal(compartmentType, request.CompartmentType);
            Assert.Equal(compartmentId, request.CompartmentId);
            Assert.Equal(resourceType != "*" ? resourceType : null, request.ResourceType);

            var headers = QueryHelpers.ParseQuery(query);
            Assert.Equal(headers.Count, request.Queries?.Count ?? 0);
            Assert.All(
                headers,
                x =>
                {
                    Assert.Contains(
                        request?.Queries,
                        y =>
                        {
                            return string.Equals(x.Key, y.Item1, StringComparison.Ordinal)
                                && string.Equals(x.Value.ToString(), y.Item2, StringComparison.Ordinal);
                        });
                });

            await _mediator.Received(1).Send<SearchCompartmentResponse>(
                Arg.Any<SearchCompartmentRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenMetadataRequest_WhenProcessingRequest_ThenGetCapabilitiesRequestShouldBeCreatedCorrectly()
        {
            var resource = new CapabilityStatement()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var httpContext = new DefaultHttpContext();
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<GetCapabilitiesResponse>(
                Arg.Any<GetCapabilitiesRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetCapabilitiesResponse(resource.ToResourceElement()));

            var request = default(GetCapabilitiesRequest);
            _mediator.When(
                x => x.Send<GetCapabilitiesResponse>(
                    Arg.Any<GetCapabilitiesRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<GetCapabilitiesRequest>());

            var response = await _fhirController.Metadata();
            var result = Assert.IsType<FhirResult>(response);
            Assert.NotNull(result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request);
            Assert.IsType<GetCapabilitiesRequest>(request);

            await _mediator.Received(1).Send<GetCapabilitiesResponse>(
                Arg.Any<GetCapabilitiesRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenWellKnownSmartConfigurationRequest_WhenProcessingRequest_ThenGetSmartConfigurationRequestShouldBeCreatedCorrectly()
        {
            var resource = new CapabilityStatement()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var baseUri = new Uri("https://example.com/fhir/");
            _requestContextAccessor.RequestContext.BaseUri.Returns(baseUri);

            var authorizeUri = new Uri("https://smart.healthit.gov/authorize");
            var tokenUri = new Uri("https://smart.healthit.gov/token");
            var httpContext = new DefaultHttpContext();
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<GetSmartConfigurationResponse>(
                Arg.Any<GetSmartConfigurationRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetSmartConfigurationResponse(
                    authorizeUri,
                    tokenUri,
                    new List<string>()));

            var request = default(GetSmartConfigurationRequest);
            _mediator.When(
                x => x.Send<GetSmartConfigurationResponse>(
                    Arg.Any<GetSmartConfigurationRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<GetSmartConfigurationRequest>());

            var response = await _fhirController.WellKnownSmartConfiguration();
            var result = Assert.IsType<OperationSmartConfigurationResult>(response);
            var smartResult = Assert.IsType<SmartConfigurationResult>(result.Value);
            Assert.NotNull(smartResult);
            Assert.Equal(authorizeUri, smartResult.AuthorizationEndpoint);
            Assert.Equal(tokenUri, smartResult.TokenEndpoint);

            Assert.NotNull(request);
            Assert.Equal(baseUri, request.BaseUri);

            await _mediator.Received(1).Send<GetSmartConfigurationResponse>(
                Arg.Any<GetSmartConfigurationRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenVersionsRequest_WhenProcessingRequest_ThenGetOperationVersionsRequestShouldBeCreatedCorrectly()
        {
            var supportedVersions = new List<string>
            {
                "4.0.1",
                "4.0.0",
                "3.0.1",
            };

            var defaultVersion = "4.0.1";
            var httpContext = new DefaultHttpContext();
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<GetOperationVersionsResponse>(
                Arg.Any<GetOperationVersionsRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetOperationVersionsResponse(supportedVersions, defaultVersion));

            var request = default(GetOperationVersionsRequest);
            _mediator.When(
                x => x.Send<GetOperationVersionsResponse>(
                    Arg.Any<GetOperationVersionsRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<GetOperationVersionsRequest>());

            var response = await _fhirController.Versions();
            var result = Assert.IsType<OperationVersionsResult>(response);
            Assert.NotNull(result.Result);
            Assert.Equal(defaultVersion, result.Result.DefaultVersion);
            Assert.All(
                supportedVersions,
                x =>
                {
                    Assert.Contains(x, result.Result.Versions);
                });

            Assert.NotNull(request);
            Assert.IsType<GetOperationVersionsRequest>(request);

            await _mediator.Received(1).Send<GetOperationVersionsResponse>(
                Arg.Any<GetOperationVersionsRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenBatchAndTransactionsRequest_WhenProcessingRequest_ThenBundleRequestShouldBeCreatedCorrectly()
        {
            var resource = new Bundle()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var responseInfo = new BundleResponseInfo(
                TimeSpan.FromSeconds(60),
                BundleType.BatchResponse,
                BundleProcessingLogic.Parallel);
            var httpContext = new DefaultHttpContext();
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<BundleResponse>(
                Arg.Any<BundleRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new BundleResponse(resource.ToResourceElement(), responseInfo));

            var request = default(BundleRequest);
            _mediator.When(
                x => x.Send<BundleResponse>(
                    Arg.Any<BundleRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<BundleRequest>());

            var response = await _fhirController.BatchAndTransactions(resource.ToResourceElement());
            var result = Assert.IsType<FhirResult>(response);
            Assert.NotNull(result.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request?.Bundle);
            Assert.Equal(resource.TypeName, request.Bundle.InstanceType);
            Assert.Equal(resource.Id, request.Bundle.Id);
            Assert.Equal(resource.VersionId, request.Bundle.VersionId);

            await _mediator.Received(1).Send<BundleResponse>(
                Arg.Any<BundleRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenCreateRequest_WhenHttpBundleInnerRequestExecutionContextIsSpecified_ThenBundleResourceContextShouldBeAddedToRequest(
            bool addHttpBundleInnerRequestExecutionContextHeader)
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
            var bundleResourceContext = new BundleResourceContext(
                Bundle.BundleType.Batch,
                BundleProcessingLogic.Parallel,
                Bundle.HTTPVerb.HEAD,
                Guid.NewGuid().ToString(),
                Guid.NewGuid());
            if (addHttpBundleInnerRequestExecutionContextHeader)
            {
                httpContext.Request.Headers[BundleOrchestratorNamingConventions.HttpBundleInnerRequestExecutionContext] =
                    JsonConvert.SerializeObject(bundleResourceContext);
            }

            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created)));

            var request = default(CreateResourceRequest);
            _mediator.When(
                x => x.Send<UpsertResourceResponse>(
                    Arg.Any<CreateResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<CreateResourceRequest>());

            await _fhirController.Create(resource.ToResourceElement());

            Assert.NotNull(request?.Resource);
            Assert.Equal(resource.TypeName, request.Resource.InstanceType);
            Assert.Equal(resource.Id, request.Resource.Id);
            Assert.Equal(resource.VersionId, request.Resource.VersionId);
            if (addHttpBundleInnerRequestExecutionContextHeader)
            {
                Assert.NotNull(request.BundleResourceContext);
                Assert.Equal(bundleResourceContext.BundleType, request.BundleResourceContext.BundleType);
                Assert.Equal(bundleResourceContext.ProcessingLogic, request.BundleResourceContext.ProcessingLogic);
                Assert.Equal(bundleResourceContext.HttpVerb, request.BundleResourceContext.HttpVerb);
                Assert.Equal(bundleResourceContext.PersistedId, request.BundleResourceContext.PersistedId);
                Assert.Equal(bundleResourceContext.BundleOperationId, request.BundleResourceContext.BundleOperationId);
            }
            else
            {
                Assert.Null(request.BundleResourceContext);
            }
        }

        private async Task RunHistoryTest(
            Func<HistoryModel, string, string, Task<IActionResult>> func,
            string typeParameter = null,
            string idParameter = null)
        {
            var resource = new Bundle()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var httpContext = new DefaultHttpContext();
            _fhirController.ControllerContext.HttpContext = httpContext;
            _mediator.Send<SearchResourceHistoryResponse>(
                Arg.Any<SearchResourceHistoryRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchResourceHistoryResponse(resource.ToResourceElement()));

            var request = default(SearchResourceHistoryRequest);
            _mediator.When(
                x => x.Send<SearchResourceHistoryResponse>(
                    Arg.Any<SearchResourceHistoryRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<SearchResourceHistoryRequest>());

            var historyModel = new HistoryModel
            {
                Since = new PartialDateTime(DateTimeOffset.UtcNow),
                Before = new PartialDateTime(DateTimeOffset.UtcNow),
                At = new PartialDateTime(DateTimeOffset.UtcNow),
                Count = 1,
                Summary = Guid.NewGuid().ToString(),
                ContinuationToken = Guid.NewGuid().ToString(),
                Sort = Guid.NewGuid().ToString(),
            };

            var response = await func(
                historyModel,
                typeParameter,
                idParameter);
            var result = Assert.IsType<FhirResult>(response);
            Assert.NotNull(result.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request);
            Assert.Equal(typeParameter, request.ResourceType);
            Assert.Equal(idParameter, request.ResourceId);
            Assert.Equal(historyModel.Since.ToString(), request.Since?.ToString());
            Assert.Equal(historyModel.Before.ToString(), request.Before?.ToString());
            Assert.Equal(historyModel.At.ToString(), request.At?.ToString());
            Assert.Equal(historyModel.Count, request.Count);
            Assert.Equal(historyModel.Summary, request.Summary);
            Assert.Equal(historyModel.ContinuationToken, request.ContinuationToken);
            Assert.Equal(historyModel.Sort, request.Sort);

            await _mediator.Received(1).Send<SearchResourceHistoryResponse>(
                Arg.Any<SearchResourceHistoryRequest>(),
                Arg.Any<CancellationToken>());
        }

        private async Task RunSearchTest(
            Func<string, Task<IActionResult>> func,
            string resourceType,
            string query)
        {
            var resource = new Bundle()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString(query);
            _fhirController.ControllerContext.HttpContext = httpContext;

            _mediator.Send<SearchResourceResponse>(
                Arg.Any<SearchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchResourceResponse(resource.ToResourceElement()));

            var request = default(SearchResourceRequest);
            _mediator.When(
                x => x.Send<SearchResourceResponse>(
                    Arg.Any<SearchResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<SearchResourceRequest>());

            var response = await func(resourceType);
            var result = Assert.IsType<FhirResult>(response);
            Assert.NotNull(result.Result);
            Assert.Equal(resource.TypeName, result.Result.InstanceType);
            Assert.Equal(resource.Id, result.Result.Id);
            Assert.Equal(resource.VersionId, result.Result.VersionId);

            Assert.NotNull(request);
            Assert.Equal(resourceType, request.ResourceType);

            var headers = QueryHelpers.ParseQuery(query);
            Assert.Equal(headers.Count, request.Queries?.Count ?? 0);
            Assert.All(
                headers,
                x =>
                {
                    Assert.Contains(
                        request?.Queries,
                        y =>
                        {
                            return string.Equals(x.Key, y.Item1, StringComparison.Ordinal)
                                && string.Equals(x.Value.ToString(), y.Item2, StringComparison.Ordinal);
                        });
                });

            await _mediator.Received(1).Send<SearchResourceResponse>(
                Arg.Any<SearchResourceRequest>(),
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
