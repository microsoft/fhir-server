// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;

namespace Microsoft.Health.Fhir.Import.Core.Messages
{
    public class GetImportResponse
    {
        public GetImportResponse(HttpStatusCode statusCode)
            : this(statusCode, taskResult: null)
        {
        }

        public GetImportResponse(HttpStatusCode statusCode, ImportTaskResult taskResult)
        {
            StatusCode = statusCode;
            TaskResult = taskResult;
        }

        /// <summary>
        /// Response http status
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Response result
        /// </summary>
        public ImportTaskResult TaskResult { get; }
    }
}
