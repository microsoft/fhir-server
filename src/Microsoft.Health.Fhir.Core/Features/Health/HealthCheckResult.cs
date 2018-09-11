// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Health
{
    /// <summary>
    /// Provides health check result.
    /// </summary>
    public class HealthCheckResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HealthCheckResult"/> class.
        /// </summary>
        /// <param name="healthState">The health state.</param>
        /// <param name="description">The description associated with the result.</param>
        public HealthCheckResult(HealthState healthState, string description)
        {
            HealthState = healthState;
            Description = description;
        }

        /// <summary>
        /// Gets the health state.
        /// </summary>
        public HealthState HealthState { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Creates an instance of <see cref="HealthCheckResult"/> representing unhealthy state.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <returns>An instance of <see cref="HealthCheckResult"/> representing unhealthy state.</returns>
        public static HealthCheckResult Unhealthy(string description)
            => new HealthCheckResult(HealthState.Unhealthy, description);

        /// <summary>
        /// Creates an instance of <see cref="HealthCheckResult"/> representing health state.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <returns>An instance of <see cref="HealthCheckResult"/> representing health state.</returns>
        public static HealthCheckResult Healthy(string description)
            => new HealthCheckResult(HealthState.Healthy, description);
    }
}
