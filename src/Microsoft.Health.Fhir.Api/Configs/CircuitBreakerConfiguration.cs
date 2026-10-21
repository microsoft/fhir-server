// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// Configuration values for memory-pressure load shedding.
    /// </summary>
    public class CircuitBreakerConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether requests are throttled when memory pressure exceeds the threshold.
        /// </summary>
        public bool EnableAutoThrottle { get; set; }

        /// <summary>
        /// Gets or sets the memory usage ratio, greater than 0 and at most 1, at which throttling starts.
        /// </summary>
        public double MemoryPressureThreshold { get; set; } = 0.85;

        /// <summary>
        /// Gets or sets the number of seconds memory pressure must stay below the threshold before throttling stops.
        /// </summary>
        public int ThrottleRecoveryTimeSeconds { get; set; } = 60;

        /// <summary>
        /// Throws when the circuit breaker is configured with an invalid value.
        /// </summary>
        public void Validate()
        {
            if (MemoryPressureThreshold <= 0 || MemoryPressureThreshold > 1)
            {
                throw new InvalidOperationException($"FhirServer:CircuitBreaker:{nameof(MemoryPressureThreshold)} must be greater than 0 and at most 1. Value: {MemoryPressureThreshold}.");
            }

            if (ThrottleRecoveryTimeSeconds < 0)
            {
                throw new InvalidOperationException($"FhirServer:CircuitBreaker:{nameof(ThrottleRecoveryTimeSeconds)} must be zero or greater. Value: {ThrottleRecoveryTimeSeconds}.");
            }
        }
    }
}
