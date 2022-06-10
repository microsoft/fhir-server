// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class GetImportResponse
    {
        public GetImportResponse(HttpStatusCode statusCode)
            : this(statusCode, jobResult: null)
        {
        }

        public GetImportResponse(HttpStatusCode statusCode, ImportJobResult jobResult)
        {
            StatusCode = statusCode;
            JobResult = jobResult;
        }

        /// <summary>
        /// Response http status
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Response result
        /// </summary>
        public ImportJobResult JobResult { get; }
    }
}
