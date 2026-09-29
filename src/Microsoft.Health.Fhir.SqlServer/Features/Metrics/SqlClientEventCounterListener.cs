// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Metrics
{
    /// <summary>
    /// Enables the SqlClient event counters in-process and forwards each counter sample to a callback.
    /// </summary>
    internal sealed class SqlClientEventCounterListener : EventListener
    {
        internal const string SqlClientEventSourceName = "Microsoft.Data.SqlClient.EventSource";

        private const string EventCountersEventName = "EventCounters";

        // Field initializers run before the base EventListener constructor, which calls OnEventSourceCreated for event sources that
        // already exist. Everything assigned in this class's constructor body is still unset during those calls.
        private readonly object _sync = new object();
        private EventSource _pendingEventSource;
        private Action<string, double> _onCounter;
        private int _intervalSeconds;

        public SqlClientEventCounterListener(int intervalSeconds, Action<string, double> onCounter)
        {
            EnsureArg.IsGt(intervalSeconds, 0, nameof(intervalSeconds));
            EnsureArg.IsNotNull(onCounter, nameof(onCounter));

            EventSource pendingEventSource;
            lock (_sync)
            {
                _intervalSeconds = intervalSeconds;
                _onCounter = onCounter;
                pendingEventSource = _pendingEventSource;
                _pendingEventSource = null;
            }

            if (pendingEventSource != null)
            {
                EnableCounters(pendingEventSource, intervalSeconds);
            }
        }

        /// <summary>
        /// Reads the counter name and value from an EventCounters payload. Polling counters report a <c>Mean</c>; incrementing polling
        /// counters report the <c>Increment</c> over the sampling interval.
        /// </summary>
        internal static bool TryGetCounterValue(IDictionary<string, object> payload, out string counterName, out double value)
        {
            counterName = null;
            value = 0;

            if (payload == null || !payload.TryGetValue("Name", out object name) || name is not string nameValue)
            {
                return false;
            }

            if ((payload.TryGetValue("Increment", out object sample) || payload.TryGetValue("Mean", out sample)) && sample is double sampleValue)
            {
                counterName = nameValue;
                value = sampleValue;
                return true;
            }

            return false;
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (!string.Equals(eventSource.Name, SqlClientEventSourceName, StringComparison.Ordinal))
            {
                return;
            }

            int intervalSeconds;
            lock (_sync)
            {
                if (_onCounter == null)
                {
                    _pendingEventSource = eventSource;
                    return;
                }

                intervalSeconds = _intervalSeconds;
            }

            EnableCounters(eventSource, intervalSeconds);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (!string.Equals(eventData.EventName, EventCountersEventName, StringComparison.Ordinal)
                || eventData.Payload == null
                || eventData.Payload.Count == 0
                || eventData.Payload[0] is not IDictionary<string, object> payload)
            {
                return;
            }

            if (TryGetCounterValue(payload, out string counterName, out double value))
            {
                _onCounter?.Invoke(counterName, value);
            }
        }

        private void EnableCounters(EventSource eventSource, int intervalSeconds)
        {
            // SqlClient writes its tracing at Informational and Verbose. Enabling at Critical keeps that tracing off; counter payloads are
            // written at LogAlways and are still delivered.
            EnableEvents(
                eventSource,
                EventLevel.Critical,
                EventKeywords.None,
                new Dictionary<string, string> { ["EventCounterIntervalSec"] = intervalSeconds.ToString(CultureInfo.InvariantCulture) });
        }
    }
}
