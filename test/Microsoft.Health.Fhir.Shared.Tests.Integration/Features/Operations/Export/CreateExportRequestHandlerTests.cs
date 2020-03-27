// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Export
{
    [Collection(FhirOperationTestConstants.FhirOperationTests)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class CreateExportRequestHandlerTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private static readonly Uri RequestUrl = new Uri("https://localhost/$export");
        private static readonly PartialDateTime SinceParameter = new PartialDateTime(DateTimeOffset.UtcNow);
        private static readonly Uri RequestUrlWithSince = new Uri($"https://localhost/$export?_since={SinceParameter}");

        private readonly MockClaimsExtractor _claimsExtractor = new MockClaimsExtractor();
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirStorageTestHelper _fhirStorageTestHelper;

        private CreateExportRequestHandler _createExportRequestHandler;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        public CreateExportRequestHandlerTests(FhirStorageTestsFixture fixture)
        {
            _fhirOperationDataStore = fixture.OperationDataStore;
            _fhirStorageTestHelper = fixture.TestHelper;
            _createExportRequestHandler = new CreateExportRequestHandler(_claimsExtractor, _fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);
        }

        public static IEnumerable<object[]> ExportUriForSameJobs
        {
            get
            {
                return new[]
                {
                    new object[] { RequestUrl, null },
                    new object[] { RequestUrlWithSince, SinceParameter },
                };
            }
        }

        public static IEnumerable<object[]> ExportUriForDifferentJobs
        {
            get
            {
                return new[]
                {
                    new object[] { RequestUrl, null, RequestUrlWithSince, SinceParameter },
                    new object[] { RequestUrl, null, new Uri("http://localhost/test"), null },
                    new object[] { RequestUrlWithSince, SinceParameter, new Uri("https://localhost/$export?_since=2020-01-01"), PartialDateTime.Parse("2020-01-01") },
                };
            }
        }

        public Task InitializeAsync()
        {
            return _fhirStorageTestHelper.DeleteAllExportJobRecordsAsync(_cancellationToken);
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Theory]
        [MemberData(nameof(ExportUriForSameJobs))]
        public async Task GivenThereIsNoMatchingJob_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated(Uri requestUrl, PartialDateTime since)
        {
            var request = new CreateExportRequest(requestUrl, since: since);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.JobId));
        }

        [MemberData(nameof(ExportUriForSameJobs))]
        [Theory]
        public async Task GivenThereIsAMatchingJob_WhenCreatingAnExportJob_ThenExistingJobShouldBeReturned(Uri requestUri, PartialDateTime since)
        {
            var request = new CreateExportRequest(requestUri, since: since);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            var newRequest = new CreateExportRequest(requestUri, since: since);

            CreateExportResponse newResponse = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.NotNull(newResponse);
            Assert.Equal(response.JobId, newResponse.JobId);
        }

        [MemberData(nameof(ExportUriForDifferentJobs))]
        [Theory]
        public async Task GivenDifferentRequestUrl_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated(Uri requestUri, PartialDateTime since, Uri newRequestUri, PartialDateTime newSince)
        {
            var request = new CreateExportRequest(requestUri, since: since);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            var newRequest = new CreateExportRequest(newRequestUri, since: newSince);

            CreateExportResponse newResponse = await _createExportRequestHandler.Handle(newRequest, _cancellationToken);

            Assert.NotNull(newResponse);
            Assert.NotEqual(response.JobId, newResponse.JobId);
        }

        [Fact]
        public async Task GivenDifferentRequestor_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated()
        {
            _claimsExtractor.ExtractImpl = () => new[] { KeyValuePair.Create("oid", "user1") };

            var request = new CreateExportRequest(RequestUrl);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            _claimsExtractor.ExtractImpl = () => new[] { KeyValuePair.Create("oid", "user2") };

            var newRequest = new CreateExportRequest(RequestUrl);

            CreateExportResponse newResponse = await _createExportRequestHandler.Handle(newRequest, _cancellationToken);

            Assert.NotNull(newResponse);
            Assert.NotEqual(response.JobId, newResponse.JobId);
        }

        [Fact]
        public async Task GivenThereIsAMatchingJob_WhenRequestorClaimsInDifferentOrder_ThenExistingJobShouldBeReturned()
        {
            var claim1 = KeyValuePair.Create("oid", "user1");
            var claim2 = KeyValuePair.Create("iss", "http://localhost/authority");

            _claimsExtractor.ExtractImpl = () => new[] { claim1, claim2 };

            var request = new CreateExportRequest(RequestUrl);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            _claimsExtractor.ExtractImpl = () => new[] { claim2, claim1 };

            var newRequest = new CreateExportRequest(RequestUrl);

            CreateExportResponse newResponse = await _createExportRequestHandler.Handle(newRequest, _cancellationToken);

            Assert.NotNull(newResponse);
            Assert.Equal(response.JobId, newResponse.JobId);
        }

        private class MockClaimsExtractor : IClaimsExtractor
        {
            public Func<IReadOnlyCollection<KeyValuePair<string, string>>> ExtractImpl { get; set; }

            public IReadOnlyCollection<KeyValuePair<string, string>> Extract()
            {
                if (ExtractImpl == null)
                {
                    return Array.Empty<KeyValuePair<string, string>>();
                }

                return ExtractImpl();
            }
        }
    }
}
