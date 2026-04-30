// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
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
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirStorageTestHelper _fhirStorageTestHelper;
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;

        private CreateExportRequestHandler _createExportRequestHandler;
        private ExportJobConfiguration _exportJobConfiguration;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        public CreateExportRequestHandlerTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _fhirOperationDataStore = AddListener(fixture.OperationDataStore);
            _fhirStorageTestHelper = fixture.TestHelper;

            _exportJobConfiguration = new ExportJobConfiguration();
            _exportJobConfiguration.Formats = new List<ExportJobFormatConfiguration>();
            _exportJobConfiguration.Formats.Add(new ExportJobFormatConfiguration()
            {
                Name = "test",
                Format = ExportFormatTags.ResourceName,
            });

            _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
            _searchOptionsFactory.Create(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>())
                .Returns(new SearchOptions() { UnsupportedSearchParams = new List<Tuple<string, string>>() });

            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            _createExportRequestHandler = new CreateExportRequestHandler(
                _claimsExtractor,
                _fhirOperationDataStore,
                DisabledFhirAuthorizationService.Instance,
                optionsExportConfig,
                _requestContextAccessor,
                _searchOptionsFactory,
                Substitute.For<ILogger<CreateExportRequestHandler>>(),
                true);
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

        /// <summary>
        /// 1. Invalid parameters on single resource.
        /// 2. Valid parameters on single resource.
        /// 3. Invalid parameters on multiple resources.
        /// 4. Valid parameters on multiple resources.
        /// </summary>
        public static IEnumerable<object[]> ValidateTypeFilters
        {
            get
            {
                return new[]
                {
                    new object[]
                    {
                        new Dictionary<string, IList<KeyValuePair<string, string>>>
                        {
                            {
                                "Patient",
                                new List<KeyValuePair<string, string>>
                                {
                                    new KeyValuePair<string, string>("name", "Bob"),
                                    new KeyValuePair<string, string>("bad", "bad"),
                                    new KeyValuePair<string, string>("gender", "male"),
                                    new KeyValuePair<string, string>("bad2", "bad2"),
                                }
                            },
                        },
                        new Dictionary<string, ISet<string>>
                        {
                            {
                                "Patient",
                                new HashSet<string>
                                {
                                    "bad",
                                    "bad2",
                                }
                            },
                        },
                        SearchParameterHandling.Lenient.ToString(),
                    },
                    new object[]
                    {
                        new Dictionary<string, IList<KeyValuePair<string, string>>>
                        {
                            {
                                "Patient",
                                new List<KeyValuePair<string, string>>
                                {
                                    new KeyValuePair<string, string>("name", "Bob"),
                                    new KeyValuePair<string, string>("gender", "male"),
                                    new KeyValuePair<string, string>("address", "here"),
                                    new KeyValuePair<string, string>("birthDate", "today"),
                                }
                            },
                        },
                        new Dictionary<string, ISet<string>>
                        {
                        },
                        string.Empty,
                    },
                    new object[]
                    {
                        new Dictionary<string, IList<KeyValuePair<string, string>>>
                        {
                            {
                                "Patient",
                                new List<KeyValuePair<string, string>>
                                {
                                    new KeyValuePair<string, string>("name", "Bob"),
                                    new KeyValuePair<string, string>("gender", "male"),
                                    new KeyValuePair<string, string>("address", "here"),
                                    new KeyValuePair<string, string>("birthDate", "today"),
                                }
                            },
                            {
                                "Observation",
                                new List<KeyValuePair<string, string>>
                                {
                                    new KeyValuePair<string, string>("subject", "Patient"),
                                    new KeyValuePair<string, string>("code", "code"),
                                    new KeyValuePair<string, string>("note:bad", "note"),
                                }
                            },
                        },
                        new Dictionary<string, ISet<string>>
                        {
                            {
                                "Patient",
                                new HashSet<string>
                                {
                                    "gender",
                                    "birthDate",
                                }
                            },
                            {
                                "Observation",
                                new HashSet<string>
                                {
                                    "code",
                                    "note:bad",
                                }
                            },
                        },
                        SearchParameterHandling.Lenient.ToString(),
                    },
                    new object[]
                    {
                        new Dictionary<string, IList<KeyValuePair<string, string>>>
                        {
                            {
                                "Patient",
                                new List<KeyValuePair<string, string>>
                                {
                                    new KeyValuePair<string, string>("name", "Bob"),
                                    new KeyValuePair<string, string>("gender", "male"),
                                    new KeyValuePair<string, string>("address", "here"),
                                    new KeyValuePair<string, string>("birthDate", "today"),
                                }
                            },
                        },
                        new Dictionary<string, ISet<string>>
                        {
                        },
                        SearchParameterHandling.Lenient.ToString(),
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

        [Theory]
        [InlineData(500u, 500u)] // Below server limit → preserved as-is
        [InlineData(10000u, 10000u)] // Exactly at max → preserved
        public async Task GivenARequestWithValidMaxCount_WhenCreatingAnExportJob_ThenMaxCountIsPreserved(uint requestedMaxCount, uint expectedMaxCount)
        {
            ExportJobRecord actualRecord = null;
            await _fhirOperationDataStore.CreateExportJobAsync(
                Arg.Do<ExportJobRecord>(record =>
                {
                    actualRecord = record;
                }),
                Arg.Any<CancellationToken>());

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, maxCount: requestedMaxCount);
            await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.NotNull(actualRecord);
            Assert.Equal(expectedMaxCount, actualRecord.MaximumNumberOfResourcesPerQuery);
        }

        [Fact]
        public async Task GivenARequestWithMaxCountExceedingLimit_WhenCreatingAnExportJob_ThenBadRequestIsReturned()
        {
            uint requestedMaxCount = ExportJobRecord.MaxMaximumNumberOfResourcesPerQuery + 1;
            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, maxCount: requestedMaxCount);

            await Assert.ThrowsAsync<BadRequestException>(() => _createExportRequestHandler.Handle(request, _cancellationToken));
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

        [Theory]
        [MemberData(nameof(ValidateTypeFilters))]
        public async Task GivenARequestWithFilters_WhenInvalidParameterFound_ThenABadRequestIsReturned(
            IDictionary<string, IList<KeyValuePair<string, string>>> filters,
            IDictionary<string, ISet<string>> invalidParameters,
#pragma warning disable xUnit1026 // Theory methods should use all of their parameters
            string searchParameterHandling)
#pragma warning restore xUnit1026 // Theory methods should use all of their parameters
        {
            ExportJobRecord actualRecord = null;
            await _fhirOperationDataStore.CreateExportJobAsync(
                Arg.Do<ExportJobRecord>(record =>
                {
                    actualRecord = record;
                }),
                Arg.Any<CancellationToken>());

            var filterString = new StringBuilder();
            foreach (var kv in filters)
            {
                filterString.Append($"{kv.Key}?");
                foreach (var p in kv.Value)
                {
                    filterString.Append($"{p.Key}={p.Value}&");
                }

                filterString[filterString.Length - 1] = ',';
            }

            if (filterString.Length > 0)
            {
                filterString.Remove(filterString.Length - 1, 1);
            }

            foreach (var kv in invalidParameters)
            {
                SearchOptions searchOptions = new SearchOptions
                {
                    UnsupportedSearchParams = kv.Value.Select(x => new Tuple<string, string>(x, x)).ToList().AsReadOnly(),
                };

                _searchOptionsFactory.Create(
                    Arg.Is<string>(x => x == kv.Key),
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<bool>(),
                    Arg.Any<ResourceVersionType>(),
                    Arg.Any<bool>())
                    .Returns(searchOptions);
            }

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, filters: filterString.ToString());
            try
            {
                _ = await _createExportRequestHandler.Handle(request, _cancellationToken);
                if (invalidParameters.Any())
                {
                    Assert.Fail($"{nameof(BadRequestException)} should be thrown.");
                }
            }
            catch (BadRequestException ex)
            {
                if (!invalidParameters.Any())
                {
                    Assert.Fail($"{nameof(BadRequestException)} should be not thrown.");
                }

                // Note: ex.Data should have the key with the validation method name and the value
                //       that is a list of a string array consisting of a resource type and code.
                Assert.NotNull(ex.Data?["ValidateTypeFilters"]);
                Assert.Equal(typeof(List<string[]>), ex.Data["ValidateTypeFilters"].GetType());

                var actualErrors = ((List<string[]>)ex.Data["ValidateTypeFilters"]).Select(x => $"{x[0]}.{x[1]}").OrderBy(x => x);
                var expectedErrors = invalidParameters.SelectMany(x => x.Value, (x, v) => $"{x.Key}.{v}").OrderBy(x => x);
                Assert.Equal(expectedErrors, actualErrors);
            }
        }

        [Theory]
        [MemberData(nameof(ValidateTypeFilters))]
        public async Task GivenARequestWithFilters_WhenInvalidParameterFoundWithLenientHandlingSpecified_ThenValidateTypeFiltersShouldBeSkipped(
            IDictionary<string, IList<KeyValuePair<string, string>>> filters,
            IDictionary<string, ISet<string>> invalidParameters,
            string searchParameterHandling)
        {
            ExportJobRecord actualRecord = null;
            await _fhirOperationDataStore.CreateExportJobAsync(
                Arg.Do<ExportJobRecord>(record =>
                {
                    actualRecord = record;
                }),
                Arg.Any<CancellationToken>());

            var requestContextHeaders = new Dictionary<string, StringValues>();
            if (!string.IsNullOrEmpty(searchParameterHandling))
            {
                requestContextHeaders.Add(KnownHeaders.Prefer, new StringValues($"handling={searchParameterHandling}"));
            }

            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            fhirRequestContext.RequestHeaders.Returns(requestContextHeaders);
            _requestContextAccessor.RequestContext.Returns(fhirRequestContext);

            var filterString = new StringBuilder();
            foreach (var kv in filters)
            {
                filterString.Append($"{kv.Key}?");
                foreach (var p in kv.Value)
                {
                    filterString.Append($"{p.Key}={p.Value}&");
                }

                filterString[filterString.Length - 1] = ',';
            }

            if (filterString.Length > 0)
            {
                filterString.Remove(filterString.Length - 1, 1);
            }

            foreach (var kv in invalidParameters)
            {
                SearchOptions searchOptions = new SearchOptions
                {
                    UnsupportedSearchParams = kv.Value.Select(x => new Tuple<string, string>(x, x)).ToList().AsReadOnly(),
                };

                _searchOptionsFactory.Create(
                    Arg.Is<string>(x => x == kv.Key),
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<bool>(),
                    Arg.Any<ResourceVersionType>(),
                    Arg.Any<bool>())
                    .Returns(searchOptions);
            }

            var request = new CreateExportRequest(RequestUrl, ExportJobType.All, filters: filterString.ToString());
            _ = await _createExportRequestHandler.Handle(request, _cancellationToken);
            _searchOptionsFactory.DidNotReceiveWithAnyArgs();
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

        /// <summary>
        /// SQL path: When an OOM occurs during export, the surrogate ID range should be split
        /// via GetSurrogateIdRanges and retried with a reduced batch size.
        /// </summary>
        [SkippableFact]
        public async Task GivenSqlExportJob_WhenOomOccurs_ThenSplitsRangeAndRetriesWithReducedBatchSize()
        {
            Skip.IfNot(_fixture.FhirRuntimeConfiguration is AzureHealthDataServicesRuntimeConfiguration, "SQL Server only test.");

            const string resourceType = "Patient";
            int callCount = 0;
            uint capturedBatchSize = 0;

            IExportJobTask MakeMockJobWithOomOnFirstCall()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
                {
                    callCount++;
                    var record = x.ArgAt<ExportJobRecord>(0);

                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM during export");
                    }

                    capturedBatchSize = record.MaximumNumberOfResourcesPerQuery;
                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                    resourceType,
                    100,
                    1000,
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    true,
                    Arg.Any<CancellationToken>(),
                    true)
                .Returns(Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(new List<(long, long, int)>
                {
                    (100, 1000, 50),
                }));

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(
                MakeMockJobWithOomOnFirstCall,
                factory,
                new TestQueueClient(),
                NullLogger<ExportProcessingJob>.Instance);

            var jobRecord = GenerateExportProcessingJobRecord(
                resourceType: resourceType,
                startSurrogateId: "100",
                endSurrogateId: "1000");
            var jobInfo = GenerateExportProcessingJobInfo(jobRecord);

            var result = await processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(callCount >= 2, $"Expected at least 2 calls (1 OOM + 1 retry), but got {callCount}");

            await searchService.Received(1).GetSurrogateIdRanges(
                resourceType,
                100,
                1000,
                Arg.Any<int>(),
                Arg.Any<int>(),
                true,
                Arg.Any<CancellationToken>(),
                true);

            // Verify batch size was reduced by factor of 10 (100 / 10 = 10)
            Assert.Equal(10u, capturedBatchSize);
        }

        /// <summary>
        /// Non-SQL path: When an OOM occurs during export, the batch size should be reduced
        /// and the export retried without surrogate ID range splitting.
        /// </summary>
        [Fact]
        public async Task GivenNonSqlExportJob_WhenOomOccurs_ThenReducesBatchSizeAndRetries()
        {
            int callCount = 0;
            uint capturedBatchSize = 0;

            IExportJobTask MakeMockJobWithOomOnFirstCall()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
                {
                    callCount++;
                    var record = x.ArgAt<ExportJobRecord>(0);

                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM during export");
                    }

                    capturedBatchSize = record.MaximumNumberOfResourcesPerQuery;
                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(
                MakeMockJobWithOomOnFirstCall,
                factory,
                new TestQueueClient(),
                NullLogger<ExportProcessingJob>.Instance);

            // No surrogate IDs = non-SQL path
            var jobRecord = GenerateExportProcessingJobRecord(resourceType: "Observation");
            var jobInfo = GenerateExportProcessingJobInfo(jobRecord);

            var result = await processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(2, callCount);
            Assert.Equal(10u, capturedBatchSize);

            await searchService.DidNotReceive().GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>());
        }

        /// <summary>
        /// SQL path: When OOM persists beyond MaxOomReductionsBeforeSoftFail reductions,
        /// the job should fail with RequestEntityTooLarge status.
        /// </summary>
        [SkippableFact]
        public async Task GivenSqlExportJob_WhenOomExceedsMaxReductions_ThenFailsWithRequestEntityTooLarge()
        {
            Skip.IfNot(_fixture.FhirRuntimeConfiguration is AzureHealthDataServicesRuntimeConfiguration, "SQL Server only test.");

            IExportJobTask MakeMockJobThatAlwaysOoms()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns<Task>(_ =>
                {
                    throw new OutOfMemoryException("Persistent OOM");
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                    Arg.Any<string>(),
                    Arg.Any<long>(),
                    Arg.Any<long>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(new List<(long, long, int)>
                {
                    (100, 500, 50),
                    (500, 1000, 50),
                }));

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(
                MakeMockJobThatAlwaysOoms,
                factory,
                new TestQueueClient(),
                NullLogger<ExportProcessingJob>.Instance);

            var jobRecord = GenerateExportProcessingJobRecord(
                resourceType: "Patient",
                startSurrogateId: "100",
                endSurrogateId: "1000");
            var jobInfo = GenerateExportProcessingJobInfo(jobRecord);

            var ex = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(jobInfo, CancellationToken.None));

            var failedRecord = (ExportJobRecord)ex.Error;
            Assert.Equal(OperationStatus.Failed, failedRecord.Status);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, failedRecord.FailureDetails.FailureStatusCode);
        }

        /// <summary>
        /// Non-SQL path: When OOM persists beyond MaxOomReductionsBeforeSoftFail reductions,
        /// the job should fail with RequestEntityTooLarge status.
        /// </summary>
        [Fact]
        public async Task GivenNonSqlExportJob_WhenOomExceedsMaxReductions_ThenFailsWithRequestEntityTooLarge()
        {
            IExportJobTask MakeMockJobThatAlwaysOoms()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns<Task>(_ =>
                {
                    throw new OutOfMemoryException("Persistent OOM");
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(
                MakeMockJobThatAlwaysOoms,
                factory,
                new TestQueueClient(),
                NullLogger<ExportProcessingJob>.Instance);

            var jobRecord = GenerateExportProcessingJobRecord(
                resourceType: "Patient",
                maximumNumberOfResourcesPerQuery: 10000);
            var jobInfo = GenerateExportProcessingJobInfo(jobRecord);

            var ex = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(jobInfo, CancellationToken.None));

            var failedRecord = (ExportJobRecord)ex.Error;
            Assert.Equal(OperationStatus.Failed, failedRecord.Status);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, failedRecord.FailureDetails.FailureStatusCode);

            // With batch size 10000, after 4 reductions (10000->1000->100->10->1), batch should reach minimum of 1
            Assert.Equal(
                1u,
                failedRecord.MaximumNumberOfResourcesPerQuery);
        }

        private static string GenerateExportProcessingJobRecord(
            string resourceType = null,
            string startSurrogateId = null,
            string endSurrogateId = null,
            ExportJobType exportJobType = ExportJobType.All,
            uint maximumNumberOfResourcesPerQuery = 100)
        {
            var record = new ExportJobRecord(
                requestUri: new Uri("https://localhost/ExportJob/"),
                exportType: exportJobType,
                exportFormat: ExportFormatTags.ResourceName,
                resourceType: resourceType,
                filters: null,
                hash: "hash",
                rollingFileSizeInMB: 0,
                maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery);
            record.Status = OperationStatus.Completed;

            if (startSurrogateId != null)
            {
                record.StartSurrogateId = startSurrogateId;
            }

            if (endSurrogateId != null)
            {
                record.EndSurrogateId = endSurrogateId;
            }

            record.Id = string.Empty;
            return JsonConvert.SerializeObject(record);
        }

        private static JobInfo GenerateExportProcessingJobInfo(string record)
        {
            return new JobInfo
            {
                Id = Random.Shared.Next(),
                Definition = record,
            };
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
