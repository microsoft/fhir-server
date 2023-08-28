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
    public class FhirClientException : Exception, IDisposable
    {
        public FhirClientException(FhirResponse<OperationOutcome> response, HttpStatusCode healthCheck)
        {
            Response = response;
            HealthCheckResult = healthCheck;
        }

        public HttpStatusCode StatusCode => Response.StatusCode;

        public HttpResponseHeaders Headers => Response.Headers;

        public FhirResponse<OperationOutcome> Response { get; }

        public HttpContent Content => Response.Content;

        public OperationOutcome OperationOutcome => Response.Resource;

        public HttpStatusCode HealthCheckResult { get; private set; }

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
            string diagnostic = OperationOutcome?.Issue?.FirstOrDefault()?.Diagnostics;
            string requestId = Response.GetRequestId();

            bool appendResponseInfo = true;
            string responseInfo = "NO_RESPONSEINFO";
            try
            {
                if (Response.Content != null)
                {
                    responseInfo = Response.Content.ReadAsStringAsync().Result;
                }
            }
            catch (ObjectDisposedException)
            {
                appendResponseInfo = false;
                responseInfo = "RESPONSEINFO_ALREADY_DISPOSED";
            }

            bool appendRequestInfo = true;
            string requestInfo = "NO_REQUESTINFO";
            try
            {
                if (Response.Response.RequestMessage.Content != null)
                {
                    requestInfo = Response.Response.RequestMessage.Content.ReadAsStringAsync().Result;
                }
            }
            catch (ObjectDisposedException)
            {
                appendRequestInfo = false;
                requestInfo = "REQUESTINFO_ALREADY_DISPOSED";
            }

            StringBuilder message = new StringBuilder();
            message.Append(StatusCode);
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                message.Append(": ").Append(diagnostic);
            }

            message.Append(" (").Append(requestId).AppendLine(")");

            message.AppendLine("==============================================");
            message.Append("Request ID: ").AppendLine(requestId);
            message.Append("Url: (").Append(Response.Response.RequestMessage?.Method.Method ?? "NO_HTTP_METHOD_AVAILABLE").Append(") ").AppendLine(Response.Response.RequestMessage?.RequestUri.ToString() ?? "NO_URI_AVAILABLE");
            message.Append("Response code: ").Append(Response.Response.StatusCode.ToString()).Append('(').Append((int)Response.Response.StatusCode).AppendLine(")");
            message.Append("Reason phrase: ").AppendLine(Response.Response.ReasonPhrase ?? "NO_REASON_PHRASE");
            message.Append("Timestamp: ").AppendLine(DateTime.UtcNow.ToString("o"));
            message.Append("Health Check Result: ").Append(HealthCheckResult.ToString()).Append('(').Append((int)HealthCheckResult).AppendLine(")");

            if (appendResponseInfo)
            {
                message.Append("Response Info: ").AppendLine(responseInfo);
            }

            if (appendRequestInfo)
            {
                message.Append("Request Info: ").AppendLine(requestInfo);
            }

            message.AppendLine("==============================================");

            return message.ToString();
        }
    }
}
