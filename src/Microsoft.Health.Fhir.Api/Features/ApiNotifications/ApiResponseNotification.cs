// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Health.Fhir.Core.Features.Metrics;

namespace Microsoft.Health.Fhir.Api.Features.ApiNotifications
{
    /// <summary>
    /// A Mediatr message containing information about API responses.
    /// This gets emitted by the ApiNotificationMiddleware when a response is returned by the server.
    /// Consume these using Mediatr to collect stats about API responses.
    /// </summary>
    public class ApiResponseNotification : IMetricsNotification
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
        /// The authentication mechanism used while calling the FHIR server.
        /// </summary>
        public string Authentication { get; set; }

        /// <summary>
        /// The protocol used to call the FHIR server.
        /// </summary>
        public string Protocol { get; set; }
    }
}
