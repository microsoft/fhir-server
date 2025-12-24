// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class BulkDeleteControllerTests
    {
        private const string OperationResultUrl = "https://fhir/_operations/bulk-delete/0";

        private readonly BulkDeleteController _controller;
        private readonly HttpRequest _httpRequest;
        private readonly IMediator _mediator;

        public BulkDeleteControllerTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.Send<CreateBulkDeleteResponse>(
                Arg.Any<CreateBulkDeleteRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new CreateBulkDeleteResponse(0));
            _mediator.Send<GetBulkDeleteResponse>(
                Arg.Any<GetBulkDeleteRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetBulkDeleteResponse(
                    new List<Parameters.ParameterComponent>(),
                    new List<OperationOutcomeIssue>(),
                    HttpStatusCode.Accepted));
            _mediator.Send<CancelBulkDeleteResponse>(
                Arg.Any<CancelBulkDeleteRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new CancelBulkDeleteResponse(HttpStatusCode.OK));

            _httpRequest = Substitute.For<HttpRequest>();
            _httpRequest.QueryString.Returns(new QueryString(null));

            var httpContext = Substitute.For<HttpContext>();
            httpContext.Request.Returns(_httpRequest);

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveOperationResultUrl(
                Arg.Any<string>(),
                Arg.Any<string>())
                .Returns(new Uri(OperationResultUrl));

            _controller = new BulkDeleteController(
                _mediator,
                urlResolver);
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    httpContext,
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData(DeleteOperation.SoftDelete, false, null, null, true)]
        [InlineData(DeleteOperation.HardDelete, false, null, null, true)]
        [InlineData(DeleteOperation.PurgeHistory, false, null, null, true)]
        [InlineData(DeleteOperation.SoftDelete, true, null, null, true)]
        [InlineData(DeleteOperation.SoftDelete, false, "t", null, true)]
        [InlineData(DeleteOperation.SoftDelete, false, "t1,t2,t3", null, true)]
        [InlineData(DeleteOperation.SoftDelete, false, "t1,t2,t3", "?p1=v1&p2=v2&p3=v3", true)]
        public async Task GivenParameters_WhenBulkDeleting_ThenBulkDeleteShouldSucceed(
            DeleteOperation operation,
            bool removeReferences,
            string excludedResourceTypes,
            string query,
            bool valid)
        {
            await Run(
                null,
                operation == DeleteOperation.HardDelete,
                operation == DeleteOperation.PurgeHistory,
                false,
                excludedResourceTypes,
                removeReferences,
                query,
                valid,
                (model) => _controller.BulkDelete(
                    model,
                    operation == DeleteOperation.PurgeHistory,
                    removeReferences,
                    excludedResourceTypes));
        }

        [Theory]
        [InlineData(KnownResourceTypes.CareTeam, DeleteOperation.SoftDelete, false, "t1,t2,t3", "?p1=v1&p2=v2&p3=v3", true)]
        public async Task GivenParameters_WhenBulkDeleting_ThenBulkDeleteByResourceTypeShouldSucceed(
            string resoureType,
            DeleteOperation operation,
            bool removeReferences,
            string excludedResourceTypes,
            string query,
            bool valid)
        {
            await Run(
                resoureType,
                operation == DeleteOperation.HardDelete,
                operation == DeleteOperation.PurgeHistory,
                false,
                excludedResourceTypes,
                removeReferences,
                query,
                valid,
                (model) => _controller.BulkDeleteByResourceType(
                    resoureType,
                    model,
                    operation == DeleteOperation.PurgeHistory,
                    removeReferences,
                    excludedResourceTypes));
        }

        [Theory]
        [InlineData(true, null, true)]
        [InlineData(false, null, true)]
        [InlineData(true, "?_lastUpdated=lt2021-12-12", true)]
        [InlineData(true, "?_lastUpdated=lt2021-12-12&_include=DiagnosticReport:based-on:ServiceRequest&_include:iterate=ServiceRequest:encounter", false)]
        public async Task GivenParameters_WhenBulkDeleting_ThenBulkDeleteSoftDeletedShouldSucceed(
            bool purgeHistory,
            string query,
            bool valid)
        {
            await Run(
                null,
                true,
                purgeHistory,
                true,
                null,
                false,
                query,
                valid,
                _ => _controller.BulkDeleteSoftDeleted(purgeHistory));
        }

        [Theory]
        [InlineData(KnownResourceTypes.Patient, true, "?_lastUpdated=lt2021-12-12", true)]
        public async Task GivenParameters_WhenBulkDeleting_ThenBulkDeleteSoftDeletedByResourceTypeShouldSucceed(
            string resourceType,
            bool purgeHistory,
            string query,
            bool valid)
        {
            await Run(
                resourceType,
                true,
                purgeHistory,
                true,
                null,
                false,
                query,
                valid,
                _ => _controller.BulkDeleteSoftDeletedByResourceType(
                    resourceType,
                    purgeHistory));
        }

        [Theory]
        [InlineData(0, HttpStatusCode.Accepted)]
        [InlineData(1, HttpStatusCode.OK)]
        public async Task GivenParameters_WhenGettingStatus_ThenGetBulkDeleteStatusShouldSucceed(
            long id,
            HttpStatusCode statusCode)
        {
            var result = new GetBulkDeleteResponse(
                null,
                null,
                statusCode);
            _mediator.Send<GetBulkDeleteResponse>(
                Arg.Any<GetBulkDeleteRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(result);

            var request = default(GetBulkDeleteRequest);
            _mediator
                .When(x => x.Send<GetBulkDeleteResponse>(Arg.Any<GetBulkDeleteRequest>(), Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<GetBulkDeleteRequest>());

            var response = await _controller.GetBulkDeleteStatusById(id);
            Assert.NotNull(request);
            Assert.Equal(id, request.JobId);

            Assert.NotNull(response);
            Assert.IsType<JobResult>(response);

            var jobResult = (JobResult)response;
            if (statusCode == HttpStatusCode.Accepted)
            {
                Assert.Contains(
                    jobResult.Headers,
                    x =>
                    {
                        return string.Equals(x.Key, KnownHeaders.Progress, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Value.ToString(), Resources.InProgress, StringComparison.OrdinalIgnoreCase);
                    });
            }
            else
            {
                Assert.DoesNotContain(
                    jobResult.Headers,
                    x =>
                    {
                        return string.Equals(x.Key, KnownHeaders.Progress, StringComparison.OrdinalIgnoreCase);
                    });
            }

            await _mediator.Received(1).Send<GetBulkDeleteResponse>(
                Arg.Any<GetBulkDeleteRequest>(),
                Arg.Any<CancellationToken>());
        }

        private async Task Run(
            string typeParameter,
            bool hardDelete,
            bool purgeHistory,
            bool softDeleteCleanup,
            string excludedResourceTypes,
            bool removeReferences,
            string query,
            bool valid,
            Func<HardDeleteModel, Task<IActionResult>> func)
        {
            _httpRequest.QueryString.Returns(new QueryString(query));
            var hardDeleteModel = new HardDeleteModel()
            {
                HardDelete = hardDelete,
            };

            var request = default(CreateBulkDeleteRequest);
            _mediator
                .When(x => x.Send(Arg.Any<CreateBulkDeleteRequest>(), Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<CreateBulkDeleteRequest>());

            try
            {
                var response = await func(hardDeleteModel);

                Assert.True(valid);
                Validate(
                    typeParameter,
                    hardDelete,
                    softDeleteCleanup,
                    purgeHistory,
                    removeReferences,
                    excludedResourceTypes,
                    query,
                    request);
                Validate(response);
            }
            catch (RequestNotValidException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid ? 1 : 0).Send<CreateBulkDeleteResponse>(
                Arg.Any<CreateBulkDeleteRequest>(),
                Arg.Any<CancellationToken>());
        }

        private static List<Tuple<string, string>> ParseQuery(string query)
        {
            var result = new List<Tuple<string, string>>();
            if (!string.IsNullOrEmpty(query))
            {
                var parameters = HttpUtility.ParseQueryString(query);
                foreach (var k in parameters.AllKeys)
                {
                    foreach (var p in parameters.GetValues(k))
                    {
                        result.Add(Tuple.Create(k, p));
                    }
                }
            }

            return result;
        }

        private static void Validate(
            string typeParameter,
            bool hardDelete,
            bool softDeleteCleanup,
            bool purgeHistory,
            bool removeReferences,
            string excludedResourceTypes,
            string query,
            CreateBulkDeleteRequest request)
        {
            Assert.NotNull(request);

            var operation = DeleteOperation.SoftDelete;
            if (hardDelete)
            {
                operation = DeleteOperation.HardDelete;
            }
            else if (purgeHistory)
            {
                operation = DeleteOperation.PurgeHistory;
            }

            Assert.Equal(operation, request.DeleteOperation);
            Assert.Equal(typeParameter, request.ResourceType, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(softDeleteCleanup, request.IncludeSoftDeleted);
            Assert.Equal(removeReferences, request.RemoveReferences);

            var conditionalParameters = ParseQuery(query);
            Assert.Equal(conditionalParameters.Count, request.ConditionalParameters.Count);
            Assert.All(
                conditionalParameters,
                x =>
                {
                    Assert.Contains(
                        request.ConditionalParameters,
                        y =>
                        {
                            return string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
                        });
                });

            var resourceTypesExcluded = excludedResourceTypes?.Split(',').ToList() ?? new List<string>();
            Assert.Equal(resourceTypesExcluded.Count, request.ExcludedResourceTypes.Count);
            Assert.All(
                resourceTypesExcluded,
                x =>
                {
                    Assert.Contains(
                        request.ExcludedResourceTypes,
                        y =>
                        {
                            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
                        });
                });
        }

        private static void Validate(IActionResult response)
        {
            Assert.NotNull(response);
            Assert.IsType<JobResult>(response);

            var result = (JobResult)response;
            Assert.Equal(OperationResultUrl, result.Headers.ContentLocation.ToString());
        }
    }
}
