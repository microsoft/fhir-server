// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Logging.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class DefaultServiceMetricHandlerTests
    {
        private readonly IMeterFactory _meterFactory;

        public DefaultServiceMetricHandlerTests()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            _meterFactory = services.BuildServiceProvider().GetRequiredService<IMeterFactory>();
        }

        [Fact]
        public void GivenAvailabilityHandler_WhenEmitAvailability_ThenMetricIsRecorded()
        {
            var handler = new DefaultServiceMetricHandler(_meterFactory);
            var recorded = new List<long>();

            using (CreateListener(recorded))
            {
                handler.EmitAvailability(true);
            }

            Assert.Equal(new long[] { 1 }, recorded);
        }

        [Fact]
        public void GivenAvailabilityHandler_WhenServiceIsUnavailable_ThenZeroIsRecorded()
        {
            var handler = new DefaultServiceMetricHandler(_meterFactory);
            var recorded = new List<long>();

            using (CreateListener(recorded))
            {
                handler.EmitAvailability(false);
            }

            Assert.Equal(new long[] { 0 }, recorded);
        }

        [Fact]
        public void GivenMultipleEmissionsWithinOneMinute_WhenEmitAvailability_ThenOnlyOneMetricIsRecorded()
        {
            var handler = new DefaultServiceMetricHandler(_meterFactory);
            var recorded = new List<long>();
            var now = DateTimeOffset.UtcNow;

            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(now)))
            using (CreateListener(recorded))
            {
                handler.EmitAvailability(false);
                handler.EmitAvailability(false);
                handler.EmitAvailability(false);
            }

            Assert.Equal(new long[] { 0 }, recorded);
        }

        [Fact]
        public void GivenEmissionsMoreThanOneMinuteApart_WhenEmitAvailability_ThenEachIsRecorded()
        {
            var handler = new DefaultServiceMetricHandler(_meterFactory);
            var recorded = new List<long>();
            var start = DateTimeOffset.UtcNow;
            var fakeTimeProvider = new FakeTimeProvider(start);

            using (Mock.Property(() => ClockResolver.TimeProvider, fakeTimeProvider))
            using (CreateListener(recorded))
            {
                handler.EmitAvailability(true);

                fakeTimeProvider.Advance(DefaultServiceMetricHandler.AvailabilityEmissionInterval + TimeSpan.FromSeconds(1));
                handler.EmitAvailability(false);
            }

            Assert.Equal(new long[] { 1, 0 }, recorded);
        }

        private static MeterListener CreateListener(List<long> recorded)
        {
            var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BaseMeterMetricHandler.MeterName && instrument.Name == "Availability")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => recorded.Add(measurement));
            listener.Start();
            return listener;
        }
    }
}
