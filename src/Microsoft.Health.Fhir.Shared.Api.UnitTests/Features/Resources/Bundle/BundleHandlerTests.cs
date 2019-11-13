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
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using NSubstitute;
using NSubstitute.Core;
using Xunit;
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

            _bundleHandler = new BundleHandler(_httpContextAccessor, _fhirRequestContextAccessor, _fhirJsonSerializer, _fhirJsonParser, transactionHandler, _bundleHttpContextAccessor, NullLogger<BundleHandler>.Instance);
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

            void RouteAsyncFunction(CallInfo callInfo)
            {
                var routeContext = callInfo.Arg<RouteContext>();
                routeContext.Handler = context =>
                {
                    context.Response.StatusCode = 403;

                    return Task.CompletedTask;
                };
            }
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
