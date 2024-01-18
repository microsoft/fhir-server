// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Azure;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Navigation;
using Hl7.FhirPath.Sprache;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
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
using Microsoft.Health.Fhir.Core.Features;
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
    public class BundleHandlerEdgeCaseTests
    {
        private DefaultFhirRequestContext _fhirRequestContext;

        public BundleHandlerEdgeCaseTests()
        {
            _fhirRequestContext = new DefaultFhirRequestContext
            {
                BaseUri = new Uri("https://localhost/"),
                CorrelationId = Guid.NewGuid().ToString(),
                ResponseHeaders = new HeaderDictionary(),
                RequestHeaders = new HeaderDictionary(),
            };
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenABundle_WhenProcessedWithConditionalQueryMaxParallelism_TheFhirContextPropertyBagsShouldBePopulatedAsExpected(bool maxParallelism)
        {
            // #conditionalQueryParallelism

            // In this test the following steps are executed/validated:
            // 1 - When the created HTTP request contains the header "x-conditionalquery-processing-logic" set as "parallel".
            // 2 - BundleHandler's constructor recognizes the presence of the header and adds "_optimizeConcurrency" to FHIR Request Context property bag.
            // 3 - A validation is executed to ensure that the FHIR Request Context property bag contains the key "_optimizeConcurrency" as it's set with the expected value.
            // 4 - If the created HTTP request does not contain the header "x-conditionalquery-processing-logic" set as "parallel", then the key "_optimizeConcurrency"
            // is not expected in the FHIR Request Context property bag.

            var requestContext = CreateRequestContextForBundleHandlerProcessing(new BundleRequestOptions() { MaxParallelism = maxParallelism });

            var fhirContextPropertyBag = requestContext.Properties;

            if (maxParallelism)
            {
                Assert.True(fhirContextPropertyBag.ContainsKey(KnownQueryParameterNames.OptimizeConcurrency));
                Assert.Equal(true, fhirContextPropertyBag[KnownQueryParameterNames.OptimizeConcurrency]);
            }
            else
            {
                Assert.False(fhirContextPropertyBag.ContainsKey(KnownQueryParameterNames.OptimizeConcurrency));
            }
        }

        private IFhirRequestContext CreateRequestContextForBundleHandlerProcessing(BundleRequestOptions options)
        {
            IRouter router = Substitute.For<IRouter>();

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            IHttpContextAccessor httpContextAccessor = Substitute.For<IHttpContextAccessor>();

            var fhirJsonSerializer = new FhirJsonSerializer();
            var fhirJsonParser = new FhirJsonParser();

            ISearchService searchService = Substitute.For<ISearchService>();
            var resourceReferenceResolver = new ResourceReferenceResolver(searchService, new QueryStringParser());

            var transactionBundleValidatorLogger = Substitute.For<ILogger<TransactionBundleValidator>>();
            var transactionBundleValidator = new TransactionBundleValidator(resourceReferenceResolver, transactionBundleValidatorLogger);

            var bundleHttpContextAccessor = new BundleHttpContextAccessor();

            var bundleConfiguration = new BundleConfiguration();
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(bundleConfiguration);

            var bundleOrchestratorLogger = Substitute.For<ILogger<BundleOrchestrator>>();
            var bundleOrchestrator = new BundleOrchestrator(bundleOptions, bundleOrchestratorLogger);

            IFeatureCollection featureCollection = CreateFeatureCollection(router);
            var httpContext = new DefaultHttpContext(featureCollection)
            {
                Request =
                {
                    Scheme = "https",
                    Host = new HostString("localhost"),
                    PathBase = new PathString("/"),
                },
            };
            var contextualHeaderDictionary = new HeaderDictionary();
            httpContext.Request.Headers.Returns(contextualHeaderDictionary);

            if (options.MaxParallelism)
            {
                httpContext.Request.Headers[KnownHeaders.ConditionalQueryProcessingLogic] = new StringValues("parallel");
            }

            if (options.QueryLatencyOverEfficiency)
            {
                httpContext.Request.Headers[KnownHeaders.QueryLatencyOverEfficiency] = new StringValues("true");
            }

            httpContextAccessor.HttpContext.Returns(httpContext);

            var transactionHandler = Substitute.For<ITransactionHandler>();

            var resourceIdProvider = new ResourceIdProvider();

            IAuditEventTypeMapping auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();

            var mediator = Substitute.For<IMediator>();

            var bundleHandler = new BundleHandler(
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
                mediator,
                NullLogger<BundleHandler>.Instance);

            return fhirRequestContextAccessor.RequestContext;
        }

        private IFeatureCollection CreateFeatureCollection(IRouter router)
        {
            var featureCollection = Substitute.For<IFeatureCollection>();

            // Header Dictionary
            var headerFeature = new HeaderDictionary();
            featureCollection.Get<IHeaderDictionary>().Returns(headerFeature);

            // Authentication
            var httpAuthenticationFeature = Substitute.For<IHttpAuthenticationFeature>();
            featureCollection.Get<IHttpAuthenticationFeature>().Returns(httpAuthenticationFeature);

            // Routing
            var routingFeature = Substitute.For<IRoutingFeature>();
            var routeData = new RouteData();
            routeData.Routers.Add(router);
            routingFeature.RouteData.Returns(routeData);
            featureCollection.Get<IRoutingFeature>().Returns(routingFeature);

            var features = new List<KeyValuePair<Type, object>>
            {
                new KeyValuePair<Type, object>(typeof(IHeaderDictionary), headerFeature),
                new KeyValuePair<Type, object>(typeof(IHttpAuthenticationFeature), httpAuthenticationFeature),
                new KeyValuePair<Type, object>(typeof(IRoutingFeature), routingFeature),
            };

            featureCollection[typeof(IHeaderDictionary)].Returns(headerFeature);
            featureCollection[typeof(IHttpAuthenticationFeature)].Returns(httpAuthenticationFeature);
            featureCollection[typeof(IRoutingFeature)].Returns(routingFeature);

            featureCollection.GetEnumerator().Returns(features.GetEnumerator());
            return featureCollection;
        }

        private sealed class BundleRequestOptions()
        {
            public bool MaxParallelism { get; set; } = false;

            public bool QueryLatencyOverEfficiency { get; set; } = false;
        }
    }
}
