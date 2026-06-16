// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Health.Fhir.Core.Features.Metrics;

namespace Microsoft.Health.Fhir.Api.Features.ExceptionNotifications
{
    /// <summary>
    /// A MediatR message containing information about exceptions with the context of the current request.
    /// Consume these using MediatR to collect stats about exceptions.
    /// </summary>
    public class ExceptionNotification : IMetricsNotification
    {
        /// <summary>
        /// The FHIR operation being performed.
        /// </summary>
        public string FhirOperation { get; set; }

        /// <summary>
        /// The type of FHIR resource associated with this context.
        /// </summary>
        public string ResourceType { get; set; }

        /// <summary>
        /// The response HTTP status code to the request.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// The amount of time elapsed to process the request.
        /// </summary>
        public TimeSpan Latency { get; set; }

        /// <summary>
        /// The correlation id associated with the current request.
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// The the outer exception type.
        /// </summary>
        public string OuterExceptionType { get; set; }

        /// <summary>
        /// The the outer method that caused the exception.
        /// </summary>
        public string OuterMethod { get; set; }

        /// <summary>
        /// The Message property for the exception.
        /// </summary>
        public string ExceptionMessage { get; set; }

        /// <summary>
        /// The StackTrace property for the exception.
        /// </summary>
        public string StackTrace { get; set; }

        /// <summary>
        /// The InnerMostExceptionType for the exception.
        /// </summary>
        public string InnerMostExceptionType { get; set; }

        /// <summary>
        /// The InnerMostExceptionMessage property for the exception.
        /// </summary>
        public string InnerMostExceptionMessage { get; set; }

        /// <summary>
        /// The HResult property for the exception.
        /// </summary>
        public int HResult { get; set; }

        /// <summary>
        /// The Source property for the exception.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// The TargetSite property for the exception.
        /// </summary>
        public string TargetSite { get; set; }

        /// <summary>
        /// The IsRequestEntityTooLarge extension property for the exception.
        /// </summary>
        public bool IsRequestEntityTooLarge { get; set; }

        /// <summary>
        /// The IsRequestRateExceeded extension property for the exception.
        /// </summary>
        public bool IsRequestRateExceeded { get; set; }

        /// <summary>
        /// Exception that can be handled downstream for additional properties for a specific exception type.
        /// </summary>
        public Exception BaseException { get; set; }
    }
}
