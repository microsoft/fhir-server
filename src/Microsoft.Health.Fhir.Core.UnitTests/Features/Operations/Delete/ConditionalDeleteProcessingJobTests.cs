// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Delete;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Delete
{
    public class ConditionalDeleteProcessingJobTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = new FhirRequestContextAccessor();

        static ConditionalDeleteProcessingJobTests()
        {
            ModelInfoProvider
                .SetProvider(MockModelInfoProviderBuilder
                    .Create(FhirSpecification.R4)
                    .Build());
        }

        [Fact]
        public async Task GivenARequest_WhenProcessingTheDelete_ThenAllItemsAreRemoved()
        {
            // Arrange
            ConditionalDeleteProcessingJob job = CreateJob();
            JobInfo jobInfo = CreateJobInfo();
            IProgress<string> progress = Substitute.For<IProgress<string>>();

            // Mock ISearchService.SearchAsync
            _searchService.SearchAsync(default, default, default)
                .ReturnsForAnyArgs(
                    CreateSearchResult(5, null),
                    CreateSearchResult(0, string.Empty));

            _mediator.Send(Arg.Any<DeleteResourceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DeleteResourceResponse(1));

            // Act
            await job.ExecuteAsync(jobInfo, progress, CancellationToken.None);

            // Assert
            await _mediator.Received(5).Send(Arg.Any<DeleteResourceRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenARequestWithContinuationToken_WhenProcessingTheDelete_ThenAllItemsAreRemoved()
        {
            // Arrange
            ConditionalDeleteProcessingJob job = CreateJob();
            JobInfo jobInfo = CreateJobInfo();
            IProgress<string> progress = Substitute.For<IProgress<string>>();

            // Mock ISearchService.SearchAsync
            _searchService.SearchAsync(default, default, default)
                .ReturnsForAnyArgs(
                    CreateSearchResult(5, "continuation-token"),
                    CreateSearchResult(3, null),
                    CreateSearchResult(0, string.Empty));

            _mediator.Send(Arg.Any<DeleteResourceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new DeleteResourceResponse(1));

            // Act
            await job.ExecuteAsync(jobInfo, progress, CancellationToken.None);

            // Assert
            await _mediator.Received(8).Send(Arg.Any<DeleteResourceRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenARequestWithNoItemsToDelete_WhenProcessingTheDelete_ThenNoDeletesArePerformed()
        {
            // Arrange
            ConditionalDeleteProcessingJob job = CreateJob();
            JobInfo jobInfo = CreateJobInfo();
            IProgress<string> progress = Substitute.For<IProgress<string>>();

            // Mock ISearchService.SearchAsync
            _searchService.SearchAsync(default, default, default)
                .ReturnsForAnyArgs(
                    CreateSearchResult(0, null));

            // Act
            await job.ExecuteAsync(jobInfo, progress, CancellationToken.None);

            // Assert
            await _mediator.DidNotReceive().Send(Arg.Any<DeleteResourceRequest>(), Arg.Any<CancellationToken>());
        }

        private ConditionalDeleteProcessingJob CreateJob()
        {
            return new ConditionalDeleteProcessingJob(_searchService, _mediator, _contextAccessor);
        }

        private JobInfo CreateJobInfo()
        {
            var jobInfo = new JobInfo
            {
                Definition = JsonConvert.SerializeObject(
                    new ConditionalDeleteJobInfo(
                        typeId: (int)JobType.ConditionalDeleteProcessing,
                        resourceType: "Patient",
                        conditionalParameters: new List<Tuple<string, string>>
                        {
                            new("identifier", "12345"),
                        },
                        deleteOperation: DeleteOperation.HardDelete,
                        principal: new ClaimsPrincipal().ToBase64(),
                        activityId: Guid.NewGuid().ToString(),
                        requestUri: new Uri("http://localhost/fhir/Patient"),
                        baseUri: new Uri("http://localhost/fhir"))),
                Result = string.Empty,
            };

            return jobInfo;
        }

        private SearchResult CreateSearchResult(int count, string continuationToken)
        {
            var entries = new List<SearchResultEntry>();

            for (int i = 0; i < count; i++)
            {
                entries.Add(new SearchResultEntry(new ResourceWrapper(
                    $"Patient-{i}",
                    "1",
                    "Patient",
                    new RawResource("{}", FhirResourceFormat.Json, false),
                    new ResourceRequest(HttpMethod.Post),
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    null,
                    null)));
            }

            return new SearchResult(entries, continuationToken, null, Array.Empty<Tuple<string, string>>());
        }
    }
}
