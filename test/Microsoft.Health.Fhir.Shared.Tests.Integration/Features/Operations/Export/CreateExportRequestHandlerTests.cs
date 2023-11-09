// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
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
        private ExportJobConfiguration _exportJobConfiguration;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        public CreateExportRequestHandlerTests(FhirStorageTestsFixture fixture)
        {
            _fhirOperationDataStore = AddListener(fixture.OperationDataStore);
            _fhirStorageTestHelper = fixture.TestHelper;

            _exportJobConfiguration = new ExportJobConfiguration();
            _exportJobConfiguration.Formats = new List<ExportJobFormatConfiguration>();
            _exportJobConfiguration.Formats.Add(new ExportJobFormatConfiguration()
            {
                Name = "test",
                Format = ExportFormatTags.ResourceName,
            });

            IOptions<ExportJobConfiguration> optionsExportConfig = Options.Create(_exportJobConfiguration);

            var contextAccess = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            _createExportRequestHandler = new CreateExportRequestHandler(_claimsExtractor, _fhirOperationDataStore, DisabledFhirAuthorizationService.Instance, optionsExportConfig, contextAccess);
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

        public static IEnumerable<object[]> ExportFilters
        {
            get
            {
                return new[]
                {
                    new object[]
                    {
                        "Observation?status=final",
                        new List<ExportJobFilter>()
                        {
                            new ExportJobFilter("Observation", new List<Tuple<string, string>>()
                            {
                                new Tuple<string, string>("status", "final"),
                            }),
                        },
                    },
                    new object[]
                    {
                        "Observation?status:not=final&_include=Observation:subject",
                        new List<ExportJobFilter>()
                        {
                            new ExportJobFilter("Observation", new List<Tuple<string, string>>()
                            {
                                new Tuple<string, string>("status:not", "final"),
                                new Tuple<string, string>("_include", "Observation:subject"),
                            }),
                        },
                    },
                    new object[]
                    {
                        "Observation?status:not=final,Patient?address=Seattle",
                        new List<ExportJobFilter>()
                        {
                            new ExportJobFilter("Observation", new List<Tuple<string, string>>()
                            {
                                new Tuple<string, string>("status:not", "final"),
                            }),
                            new ExportJobFilter("Patient", new List<Tuple<string, string>>()
                            {
                                new Tuple<string, string>("address", "Seattle"),
                            }),
                        },
                    },
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
            var request = new CreateExportRequest(requestUrl, ExportJobType.All, since: since);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.JobId));
        }

        [MemberData(nameof(ExportUriForDifferentJobs))]
        [Theory]
        public async Task GivenDifferentRequestUrl_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated(Uri requestUri, PartialDateTime since, Uri newRequestUri, PartialDateTime newSince)
        {
            var request = new CreateExportRequest(requestUri, ExportJobType.All, since: since);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            var newRequest = new CreateExportRequest(newRequestUri, ExportJobType.All, since: newSince);

            CreateExportResponse newResponse = await _createExportRequestHandler.Handle(newRequest, _cancellationToken);

            Assert.NotNull(newResponse);
            Assert.NotEqual(response.JobId, newResponse.JobId);
        }

        [Fact]
        public async Task GivenDifferentRequestor_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated()
        {
            _claimsExtractor.ExtractImpl = () => new[] { KeyValuePair.Create("oid", "user1") };

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            _claimsExtractor.ExtractImpl = () => new[] { KeyValuePair.Create("oid", "user2") };

            var newRequest = new CreateExportRequest(RequestUrl, ExportJobType.All);

            CreateExportResponse newResponse = await _createExportRequestHandler.Handle(newRequest, _cancellationToken);

            Assert.NotNull(newResponse);
            Assert.NotEqual(response.JobId, newResponse.JobId);
        }

        [Theory]
        [InlineData("test1", ExportFormatTags.ResourceName)]
        [InlineData(null, ExportFormatTags.Id)]
        public async Task GivenARequestWithDifferentFormatNames_WhenConverted_ThenTheProperFormatStringIsReturned(string formatName, string expectedFormat)
        {
            _exportJobConfiguration.Formats.Clear();
            _exportJobConfiguration.Formats.Add(new ExportJobFormatConfiguration()
            {
                Name = "test1",
                Format = ExportFormatTags.ResourceName,
            });
            _exportJobConfiguration.Formats.Add(new ExportJobFormatConfiguration()
            {
                Name = "test2",
                Format = ExportFormatTags.Id,
                Default = true,
            });
            _exportJobConfiguration.Formats.Add(new ExportJobFormatConfiguration()
            {
                Name = "test3",
                Format = ExportFormatTags.Timestamp,
            });

            ExportJobRecord actualRecord = null;
            await _fhirOperationDataStore.CreateExportJobAsync(
                Arg.Do<ExportJobRecord>(record =>
                {
                    actualRecord = record;
                }),
                Arg.Any<CancellationToken>());

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, null, formatName: formatName);
            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.Equal(expectedFormat, actualRecord.ExportFormat);
        }

        [Theory]
        [InlineData(false, ExportFormatTags.ResourceName)]
        [InlineData(true, ExportFormatTags.Timestamp + "-" + ExportFormatTags.Id + "/" + ExportFormatTags.ResourceName)]
        public async Task GivenARequest_WhenNoFormatsAreSet_ThenHardcodedDefaultIsReturned(bool containerSpecified, string expectedFormat)
        {
            _exportJobConfiguration.Formats = null;

            ExportJobRecord actualRecord = null;
            await _fhirOperationDataStore.CreateExportJobAsync(
                Arg.Do<ExportJobRecord>(record =>
                {
                    actualRecord = record;
                }),
                Arg.Any<CancellationToken>());

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, containerName: containerSpecified ? "test" : null);
            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.Equal(expectedFormat, actualRecord.ExportFormat);
        }

        [Fact]
        public async Task GivenARequestWithANonexistantFormatName_WhenConverted_ThenABadRequestIsReturned()
        {
            var formatName = "invalid";
            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, formatName: formatName);
            var exception = await Assert.ThrowsAsync<BadRequestException>(() => _createExportRequestHandler.Handle(request, _cancellationToken));
            Assert.Equal(string.Format(Resources.ExportFormatNotFound, formatName), exception.Message);
        }

        [Theory]
        [MemberData(nameof(ExportFilters))]
        public async Task GivenARequestWithFilters_WhenConverted_ThenTheFiltersArePopulated(string filters, IList<ExportJobFilter> expectedFilters)
        {
            ExportJobRecord actualRecord = null;
            await _fhirOperationDataStore.CreateExportJobAsync(
                Arg.Do<ExportJobRecord>(record =>
                {
                    actualRecord = record;
                }),
                Arg.Any<CancellationToken>());

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, filters: filters);
            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.Collection(
                actualRecord.Filters,
                expectedFilters.Select((actFilter) => new Action<ExportJobFilter>((expFilter) =>
                {
                    Assert.Equal(expFilter.ResourceType, actFilter.ResourceType);
                    Assert.Equal(expFilter.Parameters, actFilter.Parameters);
                })).ToArray());
        }

        [Theory]
        [InlineData("bad", "bad")]
        [InlineData("Observation?code=a,b,Observation?status=final", "b")] // Doesn't support nested 'or' currently
        [InlineData("Observation?status:final", "Observation?status:final")] // Incorrect divider
        public async Task GivenARequestWithIncorectFilters_WhenConverted_ThenABadRequestIsReturned(string filters, string errorMessage)
        {
            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, filters: filters);
            var exception = await Assert.ThrowsAsync<BadRequestException>(() => _createExportRequestHandler.Handle(request, _cancellationToken));
            Assert.Equal(string.Format(Resources.TypeFilterUnparseable, errorMessage), exception.Message);
        }

        /// <summary>
        /// Adds a listener to an object so that it can be spied on.
        /// This allows objects passed in through the fixture to have method calls tracked and arguments recorded.
        /// All the calls go through the spy to the underlying object.
        /// </summary>
        /// <typeparam name="T">The type of object passed</typeparam>
        /// <param name="baseObject">The object to add a listener to</param>
        /// <returns>The object wrapped in a spy</returns>
        private T AddListener<T>(T baseObject)
        {
            Type type = typeof(T);
            T spy = (T)Substitute.For(new[] { typeof(T) }, new object[0]);
            foreach (var method in typeof(T).GetMethods())
            {
                object[] inputArgs = new object[method.GetParameters().Length];
                for (int index = 0; index < inputArgs.Length; index++)
                {
                    inputArgs[index] = default;
                }

                type.InvokeMember(method.Name, System.Reflection.BindingFlags.InvokeMethod, null, spy, inputArgs).ReturnsForAnyArgs(args =>
                {
                    return type.InvokeMember(method.Name, System.Reflection.BindingFlags.InvokeMethod, null, baseObject, args.Args());
                });
            }

            return spy;
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
