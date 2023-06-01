// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
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
        private DefaultFhirRequestContext _fhirRequestContext;

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

            ISearchService searchService = Substitute.For<ISearchService>();
            var resourceReferenceResolver = new ResourceReferenceResolver(searchService, new QueryStringParser());
            var transactionBundleValidator = new TransactionBundleValidator(resourceReferenceResolver);

            var bundleHttpContextAccessor = new BundleHttpContextAccessor();

            _bundleConfiguration = new BundleConfiguration();
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(_bundleConfiguration);

            var logger = Substitute.For<ILogger<BundleOrchestrator>>();

            var bundleOrchestrator = new BundleOrchestrator(bundleOptions, logger);

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

            var transactionHandler = Substitute.For<ITransactionHandler>();

            var resourceIdProvider = new ResourceIdProvider();

            IAuditEventTypeMapping auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();

            _mediator = Substitute.For<IMediator>();

            _bundleHandler = new BundleHandler(
                httpContextAccessor,
                fhirRequestContextAccessor,
                fhirJsonSerializer,
                fhirJsonParser,
                transactionHandler,
                bundleHttpContextAccessor,
                bundleOrchestrator,
                resourceIdProvider,
                transactionBundleValidator,
                resourceReferenceResolver,
                auditEventTypeMapping,
                bundleOptions,
                DisabledFhirAuthorizationService.Instance,
                _mediator,
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

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(BundleType.BatchResponse, bundleResource.Type);
            Assert.Equal(3, bundleResource.Entry.Count);
            Assert.Equal("403", bundleResource.Entry[0].Response.Status);
            Assert.Equal("404", bundleResource.Entry[1].Response.Status);
            Assert.Equal("200", bundleResource.Entry[2].Response.Status);
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
            await _bundleHandler.Handle(bundleRequest, default);
            Assert.Equal("4", _fhirRequestContext.ResponseHeaders[headerName].ToString());
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

            var results = notification.ApiCallResults;

            Assert.Equal(code200s, results["200"].Count());

            if (code404s > 0)
            {
                Assert.Equal(code404s, results["404"].Count());
            }
            else
            {
                Assert.Equal(1, results.Keys.Count);
            }
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
