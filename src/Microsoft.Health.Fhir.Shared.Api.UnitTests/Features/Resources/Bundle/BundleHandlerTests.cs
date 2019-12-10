// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using NSubstitute.Core;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class BundleHandlerTests
    {
        private readonly BundleHandler _bundleHandler;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly BundleHttpContextAccessor _bundleHttpContextAccessor;
        private readonly IRouter _router;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly ISearchService _searchService;

        public BundleHandlerTests()
        {
            _router = Substitute.For<IRouter>();

            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            fhirRequestContext.BaseUri.Returns(new Uri("https://localhost/"));
            fhirRequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());

            _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            _fhirRequestContextAccessor.FhirRequestContext.Returns(fhirRequestContext);

            _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

            _fhirJsonSerializer = new FhirJsonSerializer();
            _fhirJsonParser = new FhirJsonParser();

            _searchService = Substitute.For<ISearchService>();

            IFhirDataStore fhirDataStore = Substitute.For<IFhirDataStore>();
            Lazy<IConformanceProvider> conformanceProvider = Substitute.For<Lazy<IConformanceProvider>>();
            IResourceWrapperFactory resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            ResourceIdProvider resourceIdProvider = Substitute.For<ResourceIdProvider>();
            var transactionBundleValidator = new TransactionBundleValidator(fhirDataStore, conformanceProvider, resourceWrapperFactory, _searchService, resourceIdProvider);

            _bundleHttpContextAccessor = new BundleHttpContextAccessor();

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
            _httpContextAccessor.HttpContext.Returns(httpContext);

            var transactionHandler = Substitute.For<ITransactionHandler>();

            _resourceIdProvider = new ResourceIdProvider();

            _bundleHandler = new BundleHandler(_httpContextAccessor, _fhirRequestContextAccessor, _fhirJsonSerializer, _fhirJsonParser, transactionHandler, _bundleHttpContextAccessor, _resourceIdProvider, transactionBundleValidator, NullLogger<BundleHandler>.Instance);
        }

        [Fact]
        public async Task GivenAnEmptyBatchBundle_WhenProcessed_ReturnsABundleResponseWithNoEntries()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Batch,
            };

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());

            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, CancellationToken.None);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(Hl7.Fhir.Model.Bundle.BundleType.BatchResponse, bundleResource.Type);
            Assert.Empty(bundleResource.Entry);
        }

        [Fact]
        public async Task GivenABundleWithAGet_WhenNotAuthorized_ReturnsABundleResponseWithCorrectEntry()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Batch,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent
                        {
                            Method = Hl7.Fhir.Model.Bundle.HTTPVerb.GET,
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
            Assert.Equal(Hl7.Fhir.Model.Bundle.BundleType.BatchResponse, bundleResource.Type);
            Assert.Single(bundleResource.Entry);

            Hl7.Fhir.Model.Bundle.EntryComponent entryComponent = bundleResource.Entry.First();
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
                Type = Hl7.Fhir.Model.Bundle.BundleType.Batch,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.GET,
                            Url = "/Patient",
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "/Patient",
                        },
                        Resource = new Hl7.Fhir.Model.Patient
                        {
                        },
                    },
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.PUT,
                            Url = "/Patient/789",
                        },
                        Resource = new Hl7.Fhir.Model.Patient
                        {
                        },
                    },
                },
            };

            _router.When(r => r.RouteAsync(Arg.Any<RouteContext>()))
                .Do(RouteAsyncFunction);

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            BundleResponse bundleResponse = await _bundleHandler.Handle(bundleRequest, default);

            var bundleResource = bundleResponse.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Equal(Hl7.Fhir.Model.Bundle.BundleType.BatchResponse, bundleResource.Type);
            Assert.Equal(3, bundleResource.Entry.Count);
            Assert.Equal("403", bundleResource.Entry[0].Response.Status);
            Assert.Equal("404", bundleResource.Entry[1].Response.Status);
            Assert.Equal("200", bundleResource.Entry[2].Response.Status);
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditionalReferences_WhenResolved_ThenReferencesValuesAreUpdatedCorrectly()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("123", KnownResourceTypes.Patient);

            var searchResult = new SearchResult(new[] { mockSearchEntry }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);
            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            foreach (var entry in bundle.Entry)
            {
                IEnumerable<ResourceReference> references = entry.Resource.GetAllChildren<ResourceReference>();

                // Asserting the conditional reference value before resolution
                Assert.Equal("Patient?identifier=12345", references.First().Reference);

                await _bundleHandler.ResolveIntraBundleReferences(entry, referenceIdDictionary, CancellationToken.None);

                // Asserting the resolved reference value after resolution
                Assert.Equal("Patient/123", references.First().Reference);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditonalReferences_WhenNotResolved_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("123", KnownResourceTypes.Patient);
            SearchResultEntry mockSearchEntry1 = GetMockSearchEntry("123", KnownResourceTypes.Patient);

            var expectedMessage = "Given conditional reference 'Patient?identifier=12345' does not resolve to a resource.";

            var searchResult = new SearchResult(new[] { mockSearchEntry, mockSearchEntry1 }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);
            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
            foreach (var entry in bundle.Entry)
            {
                IEnumerable<ResourceReference> references = entry.Resource.GetAllChildren<ResourceReference>();
                var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _bundleHandler.ResolveIntraBundleReferences(entry, referenceIdDictionary, CancellationToken.None));
                Assert.Equal(exception.Message, expectedMessage);
            }
        }

        private static SearchResultEntry GetMockSearchEntry(string resourceId, string resourceType)
        {
            return new SearchResultEntry(
               new ResourceWrapper(
                   resourceId,
                   "1",
                   resourceType,
                   new RawResource("data", Core.Models.FhirResourceFormat.Json),
                   null,
                   DateTimeOffset.MinValue,
                   false,
                   null,
                   null,
                   null));
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
