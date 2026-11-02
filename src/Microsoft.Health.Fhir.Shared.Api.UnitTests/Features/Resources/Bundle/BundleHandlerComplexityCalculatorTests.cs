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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
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
            var requests = CreateRequests(99, HTTPVerb.POST, bodyLength: 1);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenParallelBundle_WhenPayloadsAccumulateAcrossResources_ReturnsComplexityIncludingBundleAndLargestResourceCosts(bool setContentLength)
        {
            const long mebibyte = 1024L * 1024L;
            var requests = CreateRequests(96, HTTPVerb.POST, bodyLength: mebibyte, payloadOperationCount: 2, setContentLength: setContentLength);
            Assert.All(requests[HTTPVerb.POST].GetRange(0, 2), operation =>
            {
                Assert.Equal(mebibyte, operation.Context.HttpContext.Request.Body.Length);
                Assert.Equal(setContentLength ? mebibyte : null, operation.Context.HttpContext.Request.ContentLength);
            });

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        [Fact]
        public void GivenParallelBundle_WhenValueSetOperationIsPresent_ReturnsMediumComplexity()
        {
            var requests = CreateRequests(1, HTTPVerb.POST, resourceType: KnownResourceTypes.ValueSet);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(BundleComplexity.Medium, complexity);
        }

        [Theory]
        [InlineData(199, 1, BundleComplexity.Medium)]
        [InlineData(200, 1, BundleComplexity.High)]
        [InlineData(199, 2, BundleComplexity.Medium)]
        public void GivenParallelBundle_WhenProfileOperationsArePresent_AppliesProfileRefreshCostOncePerBundle(int operationCount, int profileOperationCount, BundleComplexity expectedComplexity)
        {
            var requests = CreateRequests(operationCount, HTTPVerb.POST, profileOperationCount: profileOperationCount);

            BundleComplexity complexity = CalculateComplexity(BundleProcessingLogic.Parallel, requests);

            Assert.Equal(expectedComplexity, complexity);
        }

        private static Dictionary<HTTPVerb, List<BundleHandlerInternalOperationExecutionContext>> CreateRequests(
            int operationCount,
            HTTPVerb verb,
            int conditionalOperationCount = 0,
            long? bodyLength = null,
            int getOperationCount = 0,
            int payloadOperationCount = 1,
            string resourceType = "Patient",
            bool setContentLength = true,
            int profileOperationCount = 0)
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
                if (i < payloadOperationCount && bodyLength.HasValue)
                {
                    var httpContext = new DefaultHttpContext();
                    if (setContentLength)
                    {
                        httpContext.Request.ContentLength = bodyLength;
                    }

                    httpContext.Request.Body = new System.IO.MemoryStream();
                    httpContext.Request.Body.SetLength(bodyLength.Value);
                    routeContext = new RouteContext(httpContext);
                }

                operations.Add(new BundleHandlerInternalOperationExecutionContext(
                    requestVerb,
                    i < profileOperationCount ? KnownResourceTypes.ValueSet : resourceType,
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
