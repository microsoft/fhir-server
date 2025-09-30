// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class BundleHandlerRuntimeTests
    {
        private readonly BundleConfiguration _bundleConfiguration = new BundleConfiguration();

        [Theory]
        [InlineData(BundleType.Transaction, BundleProcessingLogic.Parallel)]
        [InlineData(BundleType.Batch, BundleProcessingLogic.Sequential)]
        public void GetDefaultBundleProcessingLogic_BatchAndTransaction_ReturnsExpectedProcessingLogic(
            BundleType bundleType,
            BundleProcessingLogic expectedDefaultProcessingLogic)
        {
            // Arrange an empty HttpContext
            var httpContext = GetHttpContext();

            // Act
            var result = BundleHandlerRuntime.GetBundleProcessingLogic(_bundleConfiguration, httpContext, bundleType);

            // Assert
            Assert.Equal(expectedDefaultProcessingLogic, result);
        }

        [Fact]
        public void GetBundleProcessingLogic_NullBundleType_ReturnsSequential()
        {
            // Arrange
            var httpContext = GetHttpContext();

            // Act
            var result = BundleHandlerRuntime.GetBundleProcessingLogic(_bundleConfiguration, httpContext, null);

            // Assert
            Assert.Equal(BundleProcessingLogic.Sequential, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("parallel")]
        [InlineData("sequential")]
        [InlineData("   PaRaLlEl   ")]
        [InlineData("   SeQuentiAl   ")]
        public void IsBundleProcessingLogicSetValid_DelegatesToHttpContext(string input)
        {
            // Arrange
            var httpContext = GetHttpContext();
            httpContext.Request.Headers.Append(BundleOrchestratorNamingConventions.HttpHeaderBundleProcessingLogic, new StringValues(input));

            // Act
            var result = BundleHandlerRuntime.IsBundleProcessingLogicValid(httpContext);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("para llel")]
        [InlineData("sequential.")]
        [InlineData("   x   ")]
        [InlineData("test")]
        public void IsBundleProcessingLogicSetValid_DelegatesToHttpContext_HandleInvalid(string input)
        {
            // Arrange
            var httpContext = GetHttpContext();
            httpContext.Request.Headers.Append(BundleOrchestratorNamingConventions.HttpHeaderBundleProcessingLogic, new StringValues(input));

            // Act
            var result = BundleHandlerRuntime.IsBundleProcessingLogicValid(httpContext);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetBundleProcessingLogic_NullHttpContext_Throws()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => BundleHandlerRuntime.GetBundleProcessingLogic(_bundleConfiguration, null, BundleType.Batch));
        }

        [Fact]
        public void IsTransactionCancelledByClient_WhenTrue()
        {
            const int timeWhenCustomerCancelledTheOperation = 4;
            const int maxTransactionExecutionTimeInSeconds = 5;

            var result = BundleHandlerRuntime.IsTransactionCancelledByClient(
                TimeSpan.FromSeconds(timeWhenCustomerCancelledTheOperation),
                new BundleConfiguration { MaxExecutionTimeInSeconds = maxTransactionExecutionTimeInSeconds },
                new CancellationToken(canceled: true));

            Assert.True(result);
        }

        [Theory]
        [InlineData(false, 10, 5)]
        [InlineData(true, 5, 5)]
        public void IsTransactionCancelledByClient_WhenFalse(bool isCancelled, int transactionElapsedTime, int maxTransactionExecutionTime)
        {
            var result = BundleHandlerRuntime.IsTransactionCancelledByClient(
                TimeSpan.FromSeconds(transactionElapsedTime),
                new BundleConfiguration { MaxExecutionTimeInSeconds = maxTransactionExecutionTime },
                new CancellationToken(canceled: isCancelled));

            Assert.False(result);
        }

        private HttpContext GetHttpContext()
        {
            var httpContext = new DefaultHttpContext()
            {
                Request =
                {
                    Scheme = "https",
                    Host = new HostString("localhost"),
                    PathBase = new PathString("/"),
                },
            };

            return httpContext;
        }
    }
}
