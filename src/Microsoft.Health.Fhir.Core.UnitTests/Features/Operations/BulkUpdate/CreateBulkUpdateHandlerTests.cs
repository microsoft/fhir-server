// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using JobConflictException = Microsoft.Health.JobManagement.JobConflictException;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkUpdate
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class CreateBulkUpdateHandlerTests
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchService _searchService;
        private readonly CreateBulkUpdateHandler _handler;
        private readonly string _testUrl = "https://test.com/";

        public CreateBulkUpdateHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _queueClient = Substitute.For<IQueueClient>();
            _searchService = Substitute.For<ISearchService>();

            _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _contextAccessor.RequestContext = new FhirRequestContext(
                method: "BulkUpdateTest",
                uriString: _testUrl,
                baseUriString: _testUrl,
                correlationId: "BulkUpdateTest",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

            _handler = new CreateBulkUpdateHandler(
                _authorizationService,
                _queueClient,
                _contextAccessor,
                _searchService,
                Substitute.For<IResourceSerializer>(),
                Substitute.For<ILogger<CreateBulkUpdateHandler>>());
        }

        [Theory]
        [MemberData(nameof(GetSearchParamsForJobCreation))]
        public async Task GivenBulkUpdateRequestWithVariousSearchParams_WhenJobCreationRequested_ThenJobIsCreated(List<Tuple<string, string>> searchParams)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>()).Returns(args =>
            {
                var definition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(args.ArgAt<string[]>(1)[0]);
                Assert.Equal(_testUrl, definition.Url);
                Assert.Equal(_testUrl, definition.BaseUrl);
                Assert.Equal((searchParams?.Count ?? 0) + 1, definition.SearchParameters.Count);
                return new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Id = 1,
                    },
                };
            });

            Parameters parameters = GenerateParameters("replace");
            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, searchParams, parameters, false);

            var response = await _handler.Handle(request, CancellationToken.None);
            Assert.NotNull(response);
            Assert.Equal(1, response.Id);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenResourceTypeIsNull_ThenJobIsCreated()
        {
            var searchParams = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>()).Returns(args =>
            {
                var definition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(args.ArgAt<string[]>(1)[0]);
                Assert.Equal(_testUrl, definition.Url);
                Assert.Equal(_testUrl, definition.BaseUrl);
                Assert.Equal(searchParams.Count + 1, definition.SearchParameters.Count);
                Assert.Null(definition.Type);

                return new List<JobInfo>()
                    {
                        new JobInfo()
                        {
                            Id = 1,
                        },
                    };
            });
            Parameters parameters = GenerateParameters("upsert");
            var request = new CreateBulkUpdateRequest(null, searchParams, parameters, false);
            var response = await _handler.Handle(request, CancellationToken.None);
            Assert.NotNull(response);
            Assert.Equal(1, response.Id);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenSearchParamsIsNull_ThenJobIsCreated()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>()).Returns(args =>
            {
                var definition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(args.ArgAt<string[]>(1)[0]);
                Assert.Equal(_testUrl, definition.Url);
                Assert.Equal(_testUrl, definition.BaseUrl);
                Assert.Single(definition.SearchParameters); // Will have _lastUpdated parameter added by the handler

                return new List<JobInfo>()
                    {
                        new JobInfo()
                        {
                            Id = 1,
                        },
                    };
            });

            Parameters parameters = GenerateParameters("replace");
            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, null, parameters, false);

            var response = await _handler.Handle(request, CancellationToken.None);
            Assert.NotNull(response);
            Assert.Equal(1, response.Id);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenQueueClientThrowsUnexpectedException_ThenExceptionIsPropagated()
        {
            var searchParams = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("Unexpected error"));

            Parameters parameters = GenerateParameters("replace");
            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, searchParams, parameters, false);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenJobCreationRequestedWhileAnotherJobAlreadyRunning_ThenBadRequestIsReturned()
        {
            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };
            Parameters parameters = GenerateParameters("replace");
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>()).Throws(new JobManagement.JobConflictException("There are other active job groups"));

            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, searchParams, parameters, false);

            var ex = await Assert.ThrowsAsync<BadRequestException>(async () => await _handler.Handle(request, CancellationToken.None));
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>());
            Assert.Equal("A bulk update job is already running.", ex.Message);
        }

        [Fact]
        public async Task GivenBulkUpdateRequestWithInvalidSearchParameter_WhenJobCreationRequested_ThenBadRequestIsReturned()
        {
            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Conflict));

            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, searchParams, null, false);

            await Assert.ThrowsAsync<BadRequestException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("SearchParameter")]
        [InlineData("StructureDefinition")]
        public async Task GivenBulkUpdateRequest_WhenResourceTypeIsExcluded_ThenBadRequestIsReturned(string resourceType)
        {
            var searchParams = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            var request = new CreateBulkUpdateRequest(resourceType, searchParams, null, false);

            var ex = await Assert.ThrowsAsync<BadRequestException>(async () => await _handler.Handle(request, CancellationToken.None));
            Assert.Equal($"Bulk update is not supported for resource type {resourceType}.", ex.Message);
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenMultipleOperationTypesAndOneIsUnsupported_ThenBadRequestIsReturned()
        {
            var searchParams = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();

            // Parameters with two operation types: one supported, one unsupported
            var parameters = new Parameters
            {
                Parameter = new List<Parameters.ParameterComponent>
                {
                    new Parameters.ParameterComponent
                    {
                        Part = new List<Parameters.ParameterComponent>
                        {
                            new Parameters.ParameterComponent
                            {
                                Name = "type",
                                Value = new FhirString("Upsert"),
                            },
                        },
                    },
                    new Parameters.ParameterComponent
                    {
                        Part = new List<Parameters.ParameterComponent>
                        {
                            new Parameters.ParameterComponent
                            {
                                Name = "type",
                                Value = new FhirString("Delete"), // Not supported
                            },
                        },
                    },
                },
            };

            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, searchParams, parameters, false);
            await Assert.ThrowsAsync<BadRequestException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenUserIsUnauthorized_ThenUnauthorizedFhirActionExceptionIsThrown()
        {
            var searchParams = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("param", "value"),
            };

            // Simulate user lacking BulkOperator permission
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Write);

            Parameters parameters = GenerateParameters("upsert");

            var request = new CreateBulkUpdateRequest(KnownResourceTypes.Patient, searchParams, parameters, false);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateRequest_WhenJobCreationFails_ThenExceptionIsThrown()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkUpdate, Arg.Any<string[]>(), Arg.Any<long?>(), true, Arg.Any<CancellationToken>()).Returns(new List<JobInfo>());
            var parameters = GenerateParameters("upsert");

            var request = new CreateBulkUpdateRequest(null, new List<Tuple<string, string>>(), parameters, false);
            await Assert.ThrowsAsync<JobNotExistException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        private static Parameters GenerateParameters(string typeValue)
        {
            return new Parameters
            {
                Parameter = new List<Parameters.ParameterComponent>
                {
                    new Parameters.ParameterComponent
                    {
                        Part = new List<Parameters.ParameterComponent>
                        {
                            new Parameters.ParameterComponent
                            {
                                Name = "type",
                                Value = new FhirString(typeValue),
                            },
                        },
                    },
                },
            };
        }

        public static IEnumerable<object[]> GetSearchParamsForJobCreation()
        {
            yield return new object[]
            {
                new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("param", "value"),
                },
            };
            yield return new object[]
            {
                new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("_lastUpdated", "value1"),
                    new Tuple<string, string>("_lastUpdated", "value2"),
                    new Tuple<string, string>("_lastUpdated", "value3"),
                },
            };
        }
    }
}
