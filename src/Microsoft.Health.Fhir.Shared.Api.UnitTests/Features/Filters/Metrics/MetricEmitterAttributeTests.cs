// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Api.Features.Filters.Metrics;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class MetricEmitterAttributeTests
    {
        [Fact]
        public async Task BundleEndpointMetricEmitterAttribute_WhenCalled_EmittsMetrics()
        {
            IBundleMetricHandler metricHandler = Substitute.For<IBundleMetricHandler>();
            metricHandler.EmitLatency(Arg.Any<BundleMetricNotification>());

            var attribute = new BundleEndpointMetricEmitterAttribute(metricHandler);

            attribute.OnActionExecuting(null);
            await Task.Delay(100);
            attribute.OnActionExecuted(null);

            metricHandler.Received(1).EmitLatency(Arg.Any<BundleMetricNotification>());
        }

        [Fact]
        public async Task SearchEndpointMetricEmitterAttribute_WhenCalled_EmittsMetrics()
        {
            ISearchMetricHandler metricHandler = Substitute.For<ISearchMetricHandler>();
            metricHandler.EmitLatency(Arg.Any<SearchMetricNotification>());

            var attribute = new SearchEndpointMetricEmitterAttribute(metricHandler);

            attribute.OnActionExecuting(null);
            await Task.Delay(100);
            attribute.OnActionExecuted(null);

            metricHandler.Received(1).EmitLatency(Arg.Any<SearchMetricNotification>());
        }

        [Fact]
        public async Task CrudEndpointMetricEmitterAttribute_WhenCalled_EmittsMetrics()
        {
            ICrudMetricHandler metricHandler = Substitute.For<ICrudMetricHandler>();
            metricHandler.EmitLatency(Arg.Any<CrudMetricNotification>());

            var attribute = new CrudEndpointMetricEmitterAttribute(metricHandler);

            attribute.OnActionExecuting(null);
            await Task.Delay(100);
            attribute.OnActionExecuted(null);

            metricHandler.Received(1).EmitLatency(Arg.Any<CrudMetricNotification>());
        }
    }
}
