// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using MediatR;

namespace Microsoft.Health.Fhir.Api.Features.ApiNotifications
{
    /// <summary>
    /// A Mediatr message containing information about API responses.
    /// This gets emitted by the ApiNotificationMiddleware when a response is returned by the server.
    /// Consume these using Mediatr to collect stats about API responses.
    /// </summary>
    public class ApiResponseNotification : INotification
    {
        public string Operation { get; set; }

        public string ResourceType { get; set; }

        public HttpStatusCode? StatusCode { get; set; }

        public TimeSpan Latency { get; set; }

        public string Authentication { get; set; }

        public string Protocol { get; set; }
    }
}
