// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Client
{
    public class FhirResponse : IDisposable
    {
        public FhirResponse(HttpResponseMessage response)
        {
            Response = response;
        }

        public HttpStatusCode StatusCode => Response.StatusCode;

        public HttpResponseHeaders Headers => Response.Headers;

        public HttpContent Content => Response.Content;

        public HttpResponseMessage Response { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Response?.Dispose();
            }
        }

        public string GetRequestId()
        {
            if (Headers != null)
            {
                if (Headers.TryGetValues("X-Request-Id", out var values))
                {
                    return values.First();
                }
            }

            return "NO_FHIR_ACTIVITY_ID_FOR_THIS_TRANSACTION";
        }

        public string GetFhirResponseDetailsAsJson()
        {
            JObject details = JObject.FromObject(new
            {
                requestUri = Response.RequestMessage?.RequestUri,
                requestId = GetRequestId(),
                statusCode = Response.StatusCode,
            });

            return details.ToString();
        }
    }
}
