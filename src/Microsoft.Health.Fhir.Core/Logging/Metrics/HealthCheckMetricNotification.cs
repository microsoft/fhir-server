// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class HealthCheckMetricNotification
    {
        public string OverallStatus { get; set; }

        public string Reason { get; set; }

        public string ArmGeoLocation { get; set; }

        public string ArmResourceId { get; set; }
    }
}