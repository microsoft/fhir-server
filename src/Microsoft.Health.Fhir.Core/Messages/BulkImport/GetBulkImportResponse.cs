// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models;

namespace Microsoft.Health.Fhir.Core.Messages.BulkImport
{
    public class GetBulkImportResponse
    {
        public GetBulkImportResponse(HttpStatusCode statusCode)
            : this(statusCode, jobResult: null)
        {
        }

        public GetBulkImportResponse(HttpStatusCode statusCode, BulkImportJobResult jobResult)
        {
            StatusCode = statusCode;
            JobResult = jobResult;
        }

        public HttpStatusCode StatusCode { get; }

        public BulkImportJobResult JobResult { get; }
    }
}
