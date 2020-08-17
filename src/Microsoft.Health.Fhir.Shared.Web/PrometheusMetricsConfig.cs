// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Web
{
    public class PrometheusMetricsConfig
    {
        public bool Enabled { get; set; } = false;

        public int Port { get; set; } = 1234;

        public string Path { get; set; } = "/metrics";

        public bool DotnetRuntimeMetrics { get; set; } = true;

        public bool HttpMetrics { get; set; } = true;

        public bool SystemMetrics { get; set; } = true;
    }
}
