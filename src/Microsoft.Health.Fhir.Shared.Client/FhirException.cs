// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

        public override string Message => FormatMessage();

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

        private string FormatMessage()
        {
            StringBuilder message = new StringBuilder();

            string diagnostic = OperationOutcome?.Issue?.FirstOrDefault()?.Diagnostics;
            string operationId = GetOperationId();

            message.Append(StatusCode);
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                message.Append(": ").Append(diagnostic);
            }

            message.Append(" (").Append(operationId).Append(')');

            message.AppendLine("==============================================");
            message.Append("Url: ").AppendLine(Response.Response.RequestMessage?.RequestUri.ToString() ?? "NO_URI_AVAILABLE");
            message.Append("Response code: ").AppendLine(Response.Response.StatusCode.ToString());
            message.Append("Reason phrase: ").AppendLine(Response.Response.ReasonPhrase ?? "NO_REASON_PHRASE");

            message.AppendLine("==============================================");

            return message.ToString();
        }

        private string GetOperationId()
        {
            if (Response.Headers.TryGetValues("X-Request-Id", out var values))
            {
                return values.First();
            }

            return "NO_FHIR_OPERATION_ID_FOR_THIS_TRANSACTION";
        }
    }
}
