// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class BundleHandlerComplexityCalculatorTests
    {
        private static readonly HashSet<string> ProfileSupportedTypes = new HashSet<string>()
        {
            KnownResourceTypes.ValueSet,
            KnownResourceTypes.StructureDefinition,
            KnownResourceTypes.CodeSystem,
        };

        [Theory]
        [InlineData(99, BundleComplexity.Low)]
        [InlineData(100, BundleComplexity.Medium)]
        [InlineData(499, BundleComplexity.Medium)]
        [InlineData(500, BundleComplexity.High)]
        [InlineData(999, BundleComplexity.High)]
        [InlineData(1000, BundleComplexity.Extreme)]
        public void GivenParallelBundle_WhenOperationCountIsAtComplexityBoundary_ReturnsExpectedComplexity(int operationCount, BundleComplexity expectedComplexity)
        {
            var requests = CreateRequests(operationCount, HTTPVerb.POST);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(expectedComplexity, complexity);
        }

        [Theory]
        [InlineData(BundleProcessingLogic.Parallel, BundleComplexity.Low)]
        [InlineData(BundleProcessingLogic.Sequential, BundleComplexity.Medium)]
        public void GivenBundleWithEightyOperations_WhenProcessingLogicChanges_ReturnsExpectedComplexity(BundleProcessingLogic processingLogic, BundleComplexity expectedComplexity)
        {
            var requests = CreateRequests(80, HTTPVerb.POST);

            BundleComplexity complexity = CalculateComplexity(processingLogic, requests);

            Assert.Equal(expectedComplexity, complexity);
        }

        [Fact]
        public void GivenParallelBundle_WhenConditionalOperationIsPresent_ReturnsExpectedComplexity()
        {
            var requests = CreateRequests(99, HTTPVerb.POST, conditionalOperationCount: 1);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        [Fact]
        public void GivenParallelBundle_WhenGetOperationsArePresent_ReturnsExpectedComplexity()
        {
            var requests = CreateRequests(98, HTTPVerb.POST, getOperationCount: 1);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        [Fact]
        public void GivenParallelBundle_WhenResourcePayloadIsSmallerThanOneMebibyte_ReturnsComplexityIncludingRoundedPayloadCost()
        {
            var requests = CreateRequests(99, HTTPVerb.POST, contentLength: 1);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        [Fact]
        public void GivenParallelBundle_WhenPayloadsAccumulateAcrossResources_ReturnsComplexityIncludingBundleAndLargestResourceCosts()
        {
            const long mebibyte = 1024L * 1024L;
            var requests = CreateRequests(96, HTTPVerb.POST, contentLength: mebibyte, payloadOperationCount: 2);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        private static Dictionary<HTTPVerb, List<BundleHandlerInternalOperationExecutionContext>> CreateRequests(
            int operationCount,
            HTTPVerb verb,
            int conditionalOperationCount = 0,
            long? contentLength = null,
            int getOperationCount = 0,
            int payloadOperationCount = 1)
        {
            var requests = new Dictionary<HTTPVerb, List<BundleHandlerInternalOperationExecutionContext>>();

            for (int i = 0; i < operationCount; i++)
            {
                HTTPVerb requestVerb = i < getOperationCount ? HTTPVerb.GET : verb;
                if (!requests.TryGetValue(requestVerb, out List<BundleHandlerInternalOperationExecutionContext> operations))
                {
                    operations = new List<BundleHandlerInternalOperationExecutionContext>();
                    requests.Add(requestVerb, operations);
                }

                RouteContext routeContext = null;
                if (i < payloadOperationCount && contentLength.HasValue)
                {
                    var httpContext = new DefaultHttpContext();
                    httpContext.Request.ContentLength = contentLength;
                    httpContext.Request.Body = new System.IO.MemoryStream();
                    httpContext.Request.Body.SetLength(contentLength.Value);
                    routeContext = new RouteContext(httpContext);
                }

                operations.Add(new BundleHandlerInternalOperationExecutionContext(
                    requestVerb,
                    "Patient",
                    routeContext,
                    i,
                    persistedId: null,
                    isConditionalOperation: i < conditionalOperationCount));
            }

            return requests;
        }

        private static BundleComplexity CalculateComplexity(
            BundleProcessingLogic processingLogic,
            Dictionary<HTTPVerb, List<BundleHandlerInternalOperationExecutionContext>> requests)
        {
            var profileResolver = Substitute.For<IProvideProfilesForValidation>();
            profileResolver.GetProfilesTypes().Returns(ProfileSupportedTypes);

            return BundleHandlerComplexityCalculator.CalculateBundleComplexity(processingLogic, requests, profileResolver, NullLogger.Instance);
        }
    }
}
