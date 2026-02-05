// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexControllerTests
    {
        private ReindexController _reindexEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private HttpContext _httpContext = new DefaultHttpContext();
        private static ReindexJobConfiguration _reindexJobConfig = new ReindexJobConfiguration() { Enabled = true };
        private IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private static IReadOnlyDictionary<string, string> _searchParameterHashMap = new Dictionary<string, string>() { { "Patient", "hash1" } };

        public ReindexControllerTests()
        {
            _reindexEnabledController = GetController(_reindexJobConfig);
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            _reindexEnabledController.ControllerContext = controllerContext;
            _urlResolver.ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>()).Returns(new System.Uri("https://test.com"));
        }

        public static TheoryData<Parameters> InvalidBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithTooManyParams(),
                GetParamsResourceWithWrongNameParam(),
                null,
            };

        public static TheoryData<Parameters> ValidBody =>
            new TheoryData<Parameters>
            {
                GetValidReindexJobPostBody(2, "patient"),
                GetValidReindexJobPostBody(null, null),
            };

        [Fact]
        public async Task GivenACreateReindexRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var reindexController = GetController(new ReindexJobConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => reindexController.CreateReindexJob(null));
        }

        [Fact]
        public async Task GivenAGetReindexRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var reindexController = GetController(new ReindexJobConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => reindexController.GetReindexJob("id"));
        }

        [Fact]
        public async Task GivenAGetReindexRequest_WhenJobExists_ThenParammetersResourceReturned()
        {
            _mediator.Send(Arg.Any<GetReindexRequest>()).Returns(Task.FromResult(GetReindexJobResponse()));

            var result = await _reindexEnabledController.GetReindexJob("id");

            await _mediator.Received().Send(
                Arg.Is<GetReindexRequest>(r => r.JobId == "id"), Arg.Any<CancellationToken>());

            var parametersResource = (((FhirResult)result).Result as ResourceElement).ResourceInstance as Parameters;
            Assert.Equal(OperationStatus.Queued.ToString(), parametersResource.Parameter.Where(x => x.Name == JobRecordProperties.Status).First().Value.ToString());
        }

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(ReindexControllerTests))]
        public async Task GivenACreateReindexRequest_WhenInvalidBodySent_ThenJobIsCreatedSuccessfully(Parameters body)
        {
            _reindexEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            _mediator.Send(Arg.Any<CreateReindexRequest>()).Returns(Task.FromResult(GetCreateReindexResponse()));

            // Should NOT throw an exception - invalid parameters are now logged but don't cause failures
            var result = await _reindexEnabledController.CreateReindexJob(body);

            // Verify that the job was created successfully despite invalid parameters
            Assert.NotNull(result);
            var fhirResult = Assert.IsType<FhirResult>(result);
            Assert.NotNull(fhirResult.Result);
        }

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(ReindexControllerTests))]
        public async Task GivenACreateReindexRequest_WithValidBody_ThenCreateReindexJobCalledWithCorrectParams(Parameters body)
        {
            _reindexEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            _mediator.Send(Arg.Any<CreateReindexRequest>()).Returns(Task.FromResult(GetCreateReindexResponse()));
            var result = await _reindexEnabledController.CreateReindexJob(body);
            await _mediator.Received().Send(
                Arg.Is<CreateReindexRequest>(
                    r => true),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();

            var parametersResource = (((FhirResult)result).Result as ResourceElement).ResourceInstance as Parameters;
            Assert.Equal(OperationStatus.Queued.ToString(), parametersResource.Parameter.Where(x => x.Name == JobRecordProperties.Status).First().Value.ToString());
            Assert.DoesNotContain(parametersResource.Parameter, x => x.Name == JobRecordProperties.Resources);
            Assert.DoesNotContain(parametersResource.Parameter, x => x.Name == JobRecordProperties.SearchParams);
            Assert.DoesNotContain(parametersResource.Parameter, x => x.Name == JobRecordProperties.TargetResourceTypes);
            Assert.DoesNotContain(parametersResource.Parameter, x => x.Name == JobRecordProperties.TargetDataStoreUsagePercentage);
            Assert.DoesNotContain(parametersResource.Parameter, x => x.Name == JobRecordProperties.TargetSearchParameterTypes);
        }

        [Fact]
        public async Task GivenAReindexSingleResourceRequest_WhenProcessing_ThenReindexSingleResourceRequestShouldBeCreatedCorrectly()
        {
            var method = HttpMethods.Post;
            _reindexEnabledController.ControllerContext.HttpContext.Request.Method = method;
            _mediator
                .Send(Arg.Any<ReindexSingleResourceRequest>())
                .Returns(new ReindexSingleResourceResponse(new Parameters().ToResourceElement()));

            var request = default(ReindexSingleResourceRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<ReindexSingleResourceRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.ArgAt<ReindexSingleResourceRequest>(0));

            var typeParameter = KnownResourceTypes.Patient;
            var idParameter = Guid.NewGuid().ToString();
            var response = await _reindexEnabledController.ReindexSingleResource(
                typeParameter,
                idParameter);

            var result = Assert.IsType<FhirResult>(response);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.Result);
            Assert.Equal(KnownResourceTypes.Parameters, result.GetResultTypeName(), StringComparer.OrdinalIgnoreCase);

            Assert.NotNull(request);
            Assert.Equal(method, request.HttpMethod);
            Assert.Equal(typeParameter, request.ResourceType);
            Assert.Equal(idParameter, request.ResourceId);

            await _mediator.Received(1).Send(
                Arg.Any<ReindexSingleResourceRequest>(),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        [Fact]
        public async Task GivenAListReindexJobsRequest_WhenProcessing_ThenGetReindexRequestShouldBeCreatedCorrectly()
        {
            var etag = WeakETag.FromVersionId("ver0");
            _mediator
                .Send(Arg.Any<GetReindexRequest>())
                .Returns(
                    x =>
                    {
                        var wrapper = new ReindexJobWrapper(
                            new ReindexJobRecord(
                                _searchParameterHashMap,
                                new List<string>(),
                                new List<string>(),
                                new List<string>(),
                                5),
                            etag);
                        return new GetReindexResponse(HttpStatusCode.OK, wrapper);
                    });

            var request = default(GetReindexRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<GetReindexRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.ArgAt<GetReindexRequest>(0));

            var response = await _reindexEnabledController.ListReindexJobs();
            var result = Assert.IsType<FhirResult>(response);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.Result);
            Assert.Equal(KnownResourceTypes.Parameters, result.GetResultTypeName(), StringComparer.OrdinalIgnoreCase);
            Assert.Contains(
                result.Headers,
                x =>
                {
                    return string.Equals(x.Key, HeaderNames.ETag, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Value.ToString(), etag.ToString(), StringComparison.OrdinalIgnoreCase);
                });

            Assert.NotNull(request);
            Assert.Null(request.JobId);

            await _mediator.Received(1).Send(
                Arg.Any<GetReindexRequest>(),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        [Fact]
        public async Task GivenACancelReindexRequest_WhenProcessing_ThenCancelReindexRequestShouldBeCreatedCorrectly()
        {
            var etag = WeakETag.FromVersionId("ver0");
            _mediator
                .Send(Arg.Any<CancelReindexRequest>())
                .Returns(
                    x =>
                    {
                        var wrapper = new ReindexJobWrapper(
                            new ReindexJobRecord(
                                _searchParameterHashMap,
                                new List<string>(),
                                new List<string>(),
                                new List<string>(),
                                5),
                            etag);
                        return new CancelReindexResponse(HttpStatusCode.OK, wrapper);
                    });

            var request = default(CancelReindexRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<CancelReindexRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.ArgAt<CancelReindexRequest>(0));

            var id = Guid.NewGuid().ToString();
            var response = await _reindexEnabledController.CancelReindex(id);
            var result = Assert.IsType<FhirResult>(response);
            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.NotNull(result.Result);
            Assert.Equal(KnownResourceTypes.Parameters, result.GetResultTypeName(), StringComparer.OrdinalIgnoreCase);
            Assert.Contains(
                result.Headers,
                x =>
                {
                    return string.Equals(x.Key, HeaderNames.ETag, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Value.ToString(), etag.ToString(), StringComparison.OrdinalIgnoreCase);
                });

            Assert.NotNull(request);
            Assert.Equal(id, request.JobId);

            await _mediator.Received(1).Send(
                Arg.Any<CancelReindexRequest>(),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        private ReindexController GetController(ReindexJobConfiguration reindexConfig)
        {
            var operationConfig = new OperationsConfiguration()
            {
                Reindex = reindexConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new ReindexController(
                _mediator,
                optionsOperationConfiguration,
                _urlResolver,
                NullLogger<ReindexController>.Instance);
        }

        private static CreateReindexResponse GetCreateReindexResponse()
        {
            var jobRecord = new ReindexJobRecord(_searchParameterHashMap, new List<string>(), new List<string>(), new List<string>(), 5);
            var jobWrapper = new ReindexJobWrapper(
                jobRecord,
                WeakETag.FromVersionId("33a64df551425fcc55e4d42a148795d9f25f89d4"));
            return new CreateReindexResponse(jobWrapper);
        }

        private static GetReindexResponse GetReindexJobResponse()
        {
            var jobRecord = new ReindexJobRecord(_searchParameterHashMap, new List<string>(), new List<string>(), new List<string>(), 5);
            var jobWrapper = new ReindexJobWrapper(
                jobRecord,
                WeakETag.FromVersionId("33a64df551425fcc55e4d42a148795d9f25f89d4"));
            return new GetReindexResponse(System.Net.HttpStatusCode.OK, jobWrapper);
        }

        private static Parameters GetValidReindexJobPostBody(int? maxConcurrency, string scope)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithWrongNameParam()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithTooManyParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }
    }
}
