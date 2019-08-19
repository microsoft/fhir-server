// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using MediatR;

namespace Microsoft.Health.Fhir.Api.Features.ApiNotifications
{
    public class ApiResponseNotification : INotification
    {
        private Stopwatch _stopwatch;

        public ApiResponseNotification()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        public string Operation { get; set; }

        public string ResourceType { get; set; }

        public HttpStatusCode? StatusCode { get; set; }

        public long LatencyMilliseconds { get; set; }

        public string Authentication { get; set; }

        public string Protocol { get; set; }

        public void SetLatency()
        {
            _stopwatch.Stop();
            LatencyMilliseconds = _stopwatch.ElapsedMilliseconds;
        }
    }
}
