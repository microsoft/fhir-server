// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Configures the startup health-check gate.
    /// </summary>
    public class StorageInitializedHealthCheckConfiguration
    {
        /// <summary>
        /// The configuration section name.
        /// </summary>
        public const string SectionName = "HealthChecks:StorageInitialization";

        /// <summary>
        /// Gets or sets the absolute backstop after which the startup gate hands off to
        /// readiness (returns Healthy) regardless of initialization state. Must satisfy the
        /// invariant: legit-init-p99 &lt; StorageInitializationTimeout &lt; k8s-startup-budget.
        /// </summary>
        public TimeSpan StorageInitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }
}
