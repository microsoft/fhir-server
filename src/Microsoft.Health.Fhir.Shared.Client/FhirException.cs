// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Client
{
    public class FhirException : Exception, IDisposable
    {
        public FhirException(FhirResponse<OperationOutcome> response)
        {
            Response = response;
        }

        public HttpStatusCode StatusCode => Response.StatusCode;

        public HttpResponseHeaders Headers => Response.Headers;

        public FhirResponse<OperationOutcome> Response { get; }

        public HttpContent Content => Response.Content;

        public OperationOutcome OperationOutcome => Response.Resource;

        public override string Message => $"{StatusCode}: {OperationOutcome?.Issue?.FirstOrDefault()?.Diagnostics} ({GetOperationId()})";

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // free managed resources
                Response?.Dispose();
            }
        }

        private string GetOperationId()
        {
            if (Response.Headers.TryGetValues("X-Request-Id", out var values))
            {
                return values.First();
            }

            return string.Empty;
        }
    }
}
