// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.Core;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class BundleHandlerTests
    {
        private readonly BundleHandler _bundleHandler;
        private readonly IRouter _router;
        private readonly BundleConfiguration _bundleConfiguration;
        private readonly IMediator _mediator;
        private readonly IBundleMetricHandler _bundleMetricHandler;
        private readonly ITransactionHandler _transactionHandler;
        private DefaultFhirRequestContext _fhirRequestContext;
        private readonly IProvideProfilesForValidation _profilesResolver;

        public BundleHandlerTests()
        {
            _router = Substitute.For<IRouter>();

            _fhirRequestContext = new DefaultFhirRequestContext
            {
                BaseUri = new Uri("https://localhost/"),
                CorrelationId = Guid.NewGuid().ToString(),
                ResponseHeaders = new HeaderDictionary(),
                RequestHeaders = new HeaderDictionary(),
            };

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            IHttpContextAccessor httpContextAccessor = Substitute.For<IHttpContextAccessor>();

            var fhirJsonSerializer = new FhirJsonSerializer();
            var fhirJsonParser = new FhirJsonParser();

            var loggerResourceReferenceResolver = Substitute.For<ILogger<ResourceReferenceResolver>>();

            ISearchService searchService = Substitute.For<ISearchService>();
            var resourceReferenceResolver = new ResourceReferenceResolver(searchService, new QueryStringParser(), loggerResourceReferenceResolver);

            var transactionBundleValidatorLogger = Substitute.For<ILogger<TransactionBundleValidator>>();
            var transactionBundleValidator = new TransactionBundleValidator(resourceReferenceResolver, transactionBundleValidatorLogger);

            var bundleHttpContextAccessor = new BundleHttpContextAccessor();

            _bundleConfiguration = new BundleConfiguration();
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(_bundleConfiguration);

            var bundleOrchestratorLogger = Substitute.For<ILogger<BundleOrchestrator>>();
            var bundleOrchestrator = new BundleOrchestrator(bundleOptions, bundleOrchestratorLogger);

            IFeatureCollection featureCollection = CreateFeatureCollection();
            var httpContext = new DefaultHttpContext(featureCollection)
            {
                Request =
                {
                    Scheme = "https",
                    Host = new HostString("localhost"),
                    PathBase = new PathString("/"),
                },
            };
            httpContextAccessor.HttpContext.Returns(httpContext);

            _transactionHandler = Substitute.For<ITransactionHandler>();

            var resourceIdProvider = new ResourceIdProvider();

            IAuditEventTypeMapping auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();

            _profilesResolver = Substitute.For<IProvideProfilesForValidation>();
            _profilesResolver.GetProfilesTypes().Returns(new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" });

            _mediator = Substitute.For<IMediator>();

            _bundleMetricHandler = Substitute.For<IBundleMetricHandler>();

            _bundleHandler = new BundleHandler(
                httpContextAccessor,
                fhirRequestContextAccessor,
                fhirJsonSerializer,
                fhirJsonParser,
                _transactionHandler,
                bundleHttpContextAccessor,
                bundleOrchestrator,
                resourceIdProvider,
                transactionBundleValidator,
                resourceReferenceResolver,
                auditEventTypeMapping,
                bundleOptions,
                DisabledFhirAuthorizationService.Instance,
                _profilesResolver,
                Substitute.For<IModelInfoProvider>(),
                Substitute.For<ISearchParameterOperations>(),
                _mediator,
                _router,
                _bundleMetricHandler,
                NullLogger<BundleHandler>.Instance);
        }

        [Fact]
        public async Task GivenAnEmptyBatchBundle_WhenProcessed_ReturnsABundleResponseWithNoEntries()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
            };

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());

            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, CancellationToken.None);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Empty(bundleResource.Entry);

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenABundleWithAGet_WhenNotAuthorized_ReturnsABundleResponseWithCorrectEntry()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.GET,
                            Url = "/Patient",
                        },
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());

            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, CancellationToken.None);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Single(bundleResource.Entry);

            EntryComponent entryComponent = bundleResource.Entry.First();
            Assert.Equal("403", entryComponent.Response.Status);

            var operationOutcome = entryComponent.Response.Outcome as OperationOutcome;
            Assert.NotNull(operationOutcome);
            Assert.Single(operationOutcome.Issue);

            var issueComponent = operationOutcome.Issue.First();

            Assert.Equal(OperationOutcome.IssueSeverity.Error, issueComponent.Severity);
            Assert.Equal(OperationOutcome.IssueType.Forbidden, issueComponent.Code);
            Assert.Equal("Authorization failed.", issueComponent.Diagnostics);

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenABundle_WhenMultipleRequests_ReturnsABundleResponseWithCorrectOrder()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.GET,
                            Url = "/Patient",
                        },
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Patient",
                        },
                        Resource = new Patient(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.PUT,
                            Url = "/Patient/789",
                        },
                        Resource = new Patient(),
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            // Ensures success sign is emitted.
            _bundleMetricHandler.Received(1).EmitSuccess();

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Equal(3, bundleResource.Entry.Count);
            Assert.Equal("403", bundleResource.Entry[0].Response.Status);
            Assert.Equal("404", bundleResource.Entry[1].Response.Status);
            Assert.Equal("200", bundleResource.Entry[2].Response.Status);

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        [Trait(Traits.Category, Categories.Profiles)]
        public async Task GivenABundle_WithMultipleProfileChanges_OnlyExecuteProfileRefreshOnce()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/StructureDefinition",
                        },
                        Resource = new StructureDefinition(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/ValueSet",
                        },
                        Resource = new ValueSet(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/ValueSet",
                        },
                        Resource = new ValueSet(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/ValueSet",
                        },
                        Resource = new ValueSet(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/CodeSystem",
                        },
                        Resource = new CodeSystem(),
                    },
                },
            };

            var localAsyncFunction = (CallInfo callInfo) =>
            {
                var routeContext = callInfo.Arg<RouteContext>();
                routeContext.Handler = context =>
                {
                    switch (context.Request.Method)
                    {
                        case "POST":
                            context.Response.StatusCode = 200;
                            break;
                        default:
                            context.Response.StatusCode = 404;
                            break;
                    }

                    return Task.CompletedTask;
                };
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(localAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Equal(5, bundleResource.Entry.Count);

            // As the bundle contains multiple profile changes (with ValueSet, StructureDefinition and CodeSystem), the profile resolver should be refreshed once.
            _profilesResolver.Received(1).Refresh();

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenABundle_WithASingleRecordAndSequential_ProcessItAsABundle()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                },
            };

            var localAsyncFunction = (CallInfo callInfo) =>
            {
                var routeContext = callInfo.Arg<RouteContext>();
                routeContext.Handler = context =>
                {
                    context.Response.StatusCode = 200;
                    return Task.CompletedTask;
                };
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(localAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            // Ensures success sign is emitted.
            _bundleMetricHandler.Received(1).EmitSuccess();

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.TransactionResponse, bundleResource.Type);
            Assert.Single(bundleResource.Entry);

            Assert.True(bundleResponse.Info.BundleType == BundleType.Transaction, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Parallel, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenATransaction_WithACrashDuringCSharpTransaction_ReturnAProperError()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                },
            };

            var localAsyncFunction = (CallInfo callInfo) =>
            {
                var routeContext = callInfo.Arg<RouteContext>();
                routeContext.Handler = context =>
                {
                    // This exception simulates a possible failure committing a C# transaction.
                    throw new InvalidOperationException("This SqlTransaction has completed; it is no longer usable.");
                };
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(localAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            FhirTransactionFailedException fhirTfe = await Assert.ThrowsAsync<FhirTransactionFailedException>(async () => await _bundleHandler.Handle(bundleRequest, default));

            Assert.True(fhirTfe.ResponseStatusCode == System.Net.HttpStatusCode.InternalServerError);
        }

        // Scenario: the inner requests succeed, but committing the C# transaction throws because the
        // ambient SqlTransaction was already zombied (e.g. by a SQL error during an earlier entry that
        // was not surfaced before reaching Complete()). At that point the real cause is gone and we only
        // see a generic "This SqlTransaction has completed" InvalidOperationException, so the handler maps
        // it to a 500. This is the fallback path - contrast with the 409 test below, where the conflict is
        // surfaced by an inner request before commit and can be mapped to a precise status.
        [Fact]
        public async Task GivenATransaction_WhenTransactionIsZombiedAtCommit_ThenHttp500IsReturned()
        {
            _bundleConfiguration.TransactionDefaultProcessingLogic = BundleProcessingLogic.Sequential;

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                },
            };

            ITransactionScope transactionScope = Substitute.For<ITransactionScope>();
            _transactionHandler.BeginTransaction().Returns(transactionScope);
            transactionScope
                .When(scope => scope.Complete())
                .Do(_ => throw new InvalidOperationException("This SqlTransaction has completed; it is no longer usable."));

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(info =>
                {
                    info.Arg<RouteContext>().Handler = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status201Created;
                        return Task.CompletedTask;
                    };
                });

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            FhirTransactionFailedException fhirTfe = await Assert.ThrowsAsync<FhirTransactionFailedException>(() => _bundleHandler.Handle(bundleRequest, default));

            Assert.Equal(HttpStatusCode.InternalServerError, fhirTfe.ResponseStatusCode);
        }

        // Scenario: an inner request fails fast with a 409 conflict (as the SQL data store now does for a
        // concurrency conflict inside an ambient transaction, throwing ResourceConflictException). Because
        // the conflict is surfaced as an entry response before the transaction is committed, the handler can
        // propagate the precise 409 to the caller instead of the generic 500 from the zombied-at-commit path
        // above. This is the user-facing behavior that the SqlServerFhirDataStore fail-fast enables.
        [Fact]
        public async Task GivenATransaction_WhenInnerRequestReturnsConflictOperationOutcome_ThenHttp409IsReturned()
        {
            _bundleConfiguration.TransactionDefaultProcessingLogic = BundleProcessingLogic.Sequential;

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(info =>
                {
                    info.Arg<RouteContext>().Handler = async context =>
                    {
                        var outcome = new OperationOutcome
                        {
                            Issue = new List<OperationOutcome.IssueComponent>
                            {
                                new OperationOutcome.IssueComponent
                                {
                                    Severity = OperationOutcome.IssueSeverity.Error,
                                    Code = OperationOutcome.IssueType.Conflict,
                                    Diagnostics = "Resource has been recently updated or added",
                                },
                            },
                        };

                        context.Response.StatusCode = StatusCodes.Status409Conflict;
                        await context.Response.WriteAsync(outcome.ToJson());
                    };
                });

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            FhirTransactionFailedException fhirTfe = await Assert.ThrowsAsync<FhirTransactionFailedException>(() => _bundleHandler.Handle(bundleRequest, default));

            Assert.Equal(HttpStatusCode.Conflict, fhirTfe.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenATransaction_WithACancellationHappens_ReturnAProperError()
        {
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                CancellationToken cancellationToken = tokenSource.Token;

                var bundle = new Hl7.Fhir.Model.Bundle
                {
                    Type = BundleType.Transaction,
                    Entry = new List<EntryComponent>
                    {
                        new EntryComponent
                        {
                            Request = new RequestComponent
                            {
                                Method = HTTPVerb.POST,
                                Url = "/Observation",
                            },
                            Resource = new Observation(),
                        },
                    },
                };

                var localAsyncFunction = (CallInfo callInfo) =>
                {
                    // Forcing the cancellation of the token and stopping the entire execution.
                    tokenSource.Cancel();
                };

                _router.When(r => r.RouteAsync(Arg.Any<RouteContext>())).Do(localAsyncFunction);

                var bundleRequest = new BundleRequest(bundle.ToResourceElement());

                // As the cancellation is requested during the bundle execution and the before the max transaction time, a FhirTransactionCancelledException is expected.
                // Resulting in a HTTP408 error.
                FhirTransactionCancelledException fhirTce = await Assert.ThrowsAsync<FhirTransactionCancelledException>(async () => await _bundleHandler.Handle(bundleRequest, cancellationToken));
                Assert.True(fhirTce.ResponseStatusCode == System.Net.HttpStatusCode.RequestTimeout);

                // Ensures failure sign is emitted.
                _bundleMetricHandler.Received(1).EmitFailure(Arg.Any<string>());
            }
        }

        [Fact]
        public async Task GivenATransactionBundleRequestWithNullUrl_WhenProcessing_ReturnsABadRequest()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.PUT,
                            Url = null,
                        },
                        Resource = new Basic { Id = "test"},
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());

            await Assert.ThrowsAsync<RequestNotValidException>(async () => await _bundleHandler.Handle(bundleRequest, default));
        }

        [Fact]
        public async Task GivenATransactionBundleRequestWithNullRequestMethod_WhenProcessing_ReturnsABadRequest()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = null,
                            Url = "/Patient",
                        },
                        Resource = new Basic { Id = "test"},
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            await Assert.ThrowsAsync<RequestNotValidException>(async () => await _bundleHandler.Handle(bundleRequest, default));
        }

        [Fact]
        public async Task GivenABundle_WhenProcessed_CertainResponseHeadersArePropagatedToOuterResponse()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                },
            };

            string headerName = "x-ms-request-charge";

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(info =>
                {
                    info.Arg<RouteContext>().Handler = context =>
                    {
                        IHeaderDictionary headers = context.Response.Headers;
                        headers.TryGetValue(headerName, out StringValues existing);
                        headers[headerName] = (existing == default(StringValues) ? 2.0 : double.Parse(existing.ToString()) + 2.0).ToString(CultureInfo.InvariantCulture);
                        return Task.CompletedTask;
                    };
                });

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);
            Assert.Equal("4", _fhirRequestContext.ResponseHeaders[headerName].ToString());

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenABundle_WhenOneRequestProducesA429_429IsRetriedThenSubsequentRequestAreSkipped()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                },
            };

            int callCount = 0;

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(info =>
                {
                    info.Arg<RouteContext>().Handler = context =>
                    {
                        callCount++;
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return Task.CompletedTask;
                    };
                });

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            Assert.Equal(2, callCount);
            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(3, bundleResource.Entry.Count);
            Assert.All(bundleResource.Entry, e => Assert.Equal("429", e.Response.Status));

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenABundle_WhenOneRequestProducesA429_429IsRetriedThenSucceeds()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                },
            };

            int callCount = 0;

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(info =>
                {
                    info.Arg<RouteContext>().Handler = context =>
                    {
                        callCount++;
                        if (callCount == 2)
                        {
                            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        }
                        else
                        {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                        }

                        return Task.CompletedTask;
                    };
                });

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            Assert.Equal(4, callCount);
            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(3, bundleResource.Entry.Count);
            foreach (var entry in bundleResource.Entry)
            {
                Assert.Equal("200", entry.Response.Status);
            }

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Theory]
        [InlineData(BundleType.Batch)]
        [InlineData(BundleType.Transaction)]
        public async Task GivenABundle_WhenOneRequestProducesA429_ThenCancelledTheRequestDuringDelay(BundleType bundleType)
        {
            const int RetryAfterSeconds = 3;
            const int CancellationAfterSeconds = 1;

            // Set Retry-After header.
            _fhirRequestContext.ResponseHeaders.Add("retry-after", RetryAfterSeconds.ToString());

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = bundleType,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.GET, Url = "/Patient" } },
                },
            };

            int callCount = 0;

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(info =>
                {
                    info.Arg<RouteContext>().Handler = context =>
                    {
                        callCount++;
                        if (callCount == 2)
                        {
                            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        }
                        else
                        {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                        }

                        return Task.CompletedTask;
                    };
                });

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(CancellationAfterSeconds));

            if (bundleType == BundleType.Batch)
            {
                BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, tokenSource.Token);

                Assert.Equal(2, callCount); // Two calls should be executed, as the second one is throttled and before retried it's cancelled.
                var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();

                Assert.Equal(3, bundleResource.Entry.Count);

                Assert.Equal("200", bundleResource.Entry[0].Response.Status);
                Assert.Equal("408", bundleResource.Entry[1].Response.Status); // Record marked as Request Timeout due to cancellation, before attempting to retry the throttled request.
                Assert.Equal("408", bundleResource.Entry[2].Response.Status); // Record marked as Request Timeout due to cancellation.

                Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
                Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
                Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
            }
            else
            {
                FhirTransactionCancelledException fhirTce = await Assert.ThrowsAsync<FhirTransactionCancelledException>(async () => await _bundleHandler.Handle(bundleRequest, tokenSource.Token));
                Assert.True(fhirTce.ResponseStatusCode == System.Net.HttpStatusCode.RequestTimeout);

                Assert.Equal(2, callCount); // Two calls should be executed, as the second one is throttled and before retried it's cancelled.

                // Ensures failure sign is emitted.
                _bundleMetricHandler.Received(1).EmitFailure(Arg.Any<string>());
            }
        }

        [Fact]
        public async Task GivenAConfigurationEntryLimit_WhenExceeded_ThenBundleEntryLimitExceededExceptionShouldBeThrown()
        {
            _bundleConfiguration.EntryLimit = 1;
            var requestBundle = Samples.GetDefaultBatch();
            var bundleRequest = new BundleRequest(requestBundle);

            var expectedMessage = "The number of entries in the bundle exceeded the configured limit of 1.";

            var exception = await Assert.ThrowsAsync<BundleEntryLimitExceededException>(async () => await _bundleHandler.Handle(bundleRequest, CancellationToken.None));
            Assert.Equal(exception.Message, expectedMessage);
        }

        [Fact]
        public async Task GivenABundleWithAnExportPost_WhenProcessed_ThenItIsProcessedCorrectly()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent { Request = new RequestComponent { Method = HTTPVerb.POST, Url = "/$export" } },
                },
            };
            var bundleRequest = new BundleRequest(bundle.ToResourceElement());

            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, CancellationToken.None);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Single(bundleResource.Entry);

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        // PUT calls are mocked to succeed while POST calls are mocked to fail.
        [Theory]
        [InlineData(BundleType.Batch, HTTPVerb.POST, HTTPVerb.PUT, 1, 1)]
        [InlineData(BundleType.Transaction, HTTPVerb.PUT, HTTPVerb.PUT, 2, 0)]
        public async Task GivenABundleWithMultipleCalls_WhenProcessed_ThenANotificationWillBeEmitted(BundleType type, HTTPVerb method1, HTTPVerb method2, int code200s, int code404s)
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = type,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = method1,
                            Url = "unused1",
                        },
                        Resource = new Patient(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = method2,
                            Url = "unused2",
                        },
                        Resource = new Patient(),
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            BundleMetricsNotification notification = null;
            await _mediator.Publish(Arg.Do<BundleMetricsNotification>(note => notification = note), Arg.Any<CancellationToken>());

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(type == BundleType.Batch ? BundleType.BatchResponse : BundleType.TransactionResponse, bundleResource.Type);
            Assert.Equal(2, bundleResource.Entry.Count);

            await _mediator.Received().Publish(Arg.Any<BundleMetricsNotification>(), Arg.Any<CancellationToken>());

            Assert.Equal(type == BundleType.Batch ? AuditEventSubType.Batch : AuditEventSubType.Transaction, notification.FhirOperation);
            Assert.Equal("https", notification.Protocol); // Verify protocol is set correctly

            var results = notification.ApiCallResults;

            Assert.Equal(code200s, results["200"].Count());

            if (code404s > 0)
            {
                Assert.Equal(code404s, results["404"].Count());
            }
            else
            {
                Assert.Single(results.Keys);
            }

            Assert.True(bundleResponse.Info.BundleType == type, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Sequential, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        [Fact]
        public async Task GivenAFailedTransaction_WhenProcessed_ThenNoNotificationWillBeEmitted()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.PUT,
                            Url = "unused1",
                        },
                        Resource = new Patient(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            // This will fail and cause an exception to be thrown
                            Method = HTTPVerb.GET,
                            Url = "unused2",
                        },
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            await Assert.ThrowsAsync<FhirTransactionFailedException>(() => _bundleHandler.Handle(bundleRequest, default));

            await _mediator.DidNotReceive().Publish(Arg.Any<BundleMetricsNotification>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenABundleRequest_WhenEmptyRequestsExist_ThenABundleResponseShouldHaveEmptyResponseComponents()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = null,
                            Url = "/Patient/123",
                        },
                        Resource = new Patient(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.GET,
                            Url = "/Patient",
                        },
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = null,
                            Url = "/Patient",
                        },
                        Resource = new Patient(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.PUT,
                            Url = "/Patient/789",
                        },
                        Resource = new Patient(),
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);

            var empties = bundleResource.Entry
                .Count(
                    x =>
                    {
                        return (x.Response?.Outcome as OperationOutcome)?.Issue?
                            .Any(
                                y =>
                                {
                                    return y.Severity == OperationOutcome.IssueSeverity.Error
                                        && y.Code == OperationOutcome.IssueType.Invalid
                                        && y.Diagnostics.Contains("Request is empty", StringComparison.OrdinalIgnoreCase);
                                }) ?? false;
                    });
            Assert.Equal(2, empties);
        }

        [Fact]
        public async Task GivenABundleRequest_WhenBatchAndParallelProcessing_ThenTheRequestShouldBeProcessedSuccessfully()
        {
            _bundleConfiguration.BatchDefaultProcessingLogic = BundleProcessingLogic.Parallel;
            _bundleConfiguration.SupportsBundleOrchestrator = true;

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Observation",
                        },
                        Resource = new Observation(),
                    },
                },
            };

            var localAsyncFunction = (CallInfo callInfo) =>
            {
                var routeContext = callInfo.Arg<RouteContext>();
                routeContext.Handler = context =>
                {
                    context.Response.StatusCode = 200;
                    return Task.CompletedTask;
                };
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(localAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Single(bundleResource.Entry);

            Assert.True(bundleResponse.Info.BundleType == BundleType.Batch, "BundleType is different than the expected.");
            Assert.True(bundleResponse.Info.ProcessingLogic == BundleProcessingLogic.Parallel, "BundleProcessingLogic is different than the expected.");
            Assert.True(bundleResponse.Info.ExecutionTime.TotalMilliseconds > 0, "ExecutionTime is not higher than zero.");
        }

        private void RouteAsyncFunction(CallInfo callInfo)
        {
            var routeContext = callInfo.Arg<RouteContext>();
            routeContext.Handler = context =>
            {
                switch (context.Request.Method)
                {
                    case "GET":
                        context.Response.StatusCode = 403;
                        break;
                    case "POST":
                        context.Response.StatusCode = 404;
                        break;
                    default:
                        context.Response.StatusCode = 200;
                        break;
                }

                return Task.CompletedTask;
            };
        }

        private IFeatureCollection CreateFeatureCollection()
        {
            var featureCollection = Substitute.For<IFeatureCollection>();

            var httpAuthenticationFeature = Substitute.For<IHttpAuthenticationFeature>();

            var routingFeature = Substitute.For<IRoutingFeature>();
            var routeData = new RouteData();
            routeData.Routers.Add(_router);
            routingFeature.RouteData.Returns(routeData);

            featureCollection.Get<IHttpAuthenticationFeature>().Returns(httpAuthenticationFeature);
            featureCollection.Get<IRoutingFeature>().Returns(routingFeature);

            var features = new List<KeyValuePair<Type, object>>
            {
                new KeyValuePair<Type, object>(typeof(IHttpAuthenticationFeature), httpAuthenticationFeature),
                new KeyValuePair<Type, object>(typeof(IRoutingFeature), routingFeature),
            };

            featureCollection[typeof(IHttpAuthenticationFeature)].Returns(httpAuthenticationFeature);
            featureCollection[typeof(IRoutingFeature)].Returns(routingFeature);

            featureCollection.GetEnumerator().Returns(features.GetEnumerator());
            return featureCollection;
        }
    }
}
