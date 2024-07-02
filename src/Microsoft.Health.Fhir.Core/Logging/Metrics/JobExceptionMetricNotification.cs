// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class JobExceptionMetricNotification : IJobExceptionMetricNotification
    {
        public string JobType { get; set; }

        public string Severity { get; set; }

        public string ExceptionType { get; set; }
    }
}
