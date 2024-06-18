// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class ExceptionMetricNotification : IExceptionMetricNotification
    {
        public string OperationName { get; set; }

        public string ExceptionType { get; set; }

        public string Severity { get; set; }
    }
}
