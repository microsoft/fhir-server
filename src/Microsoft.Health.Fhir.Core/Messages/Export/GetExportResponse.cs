// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class GetExportResponse
    {
        public GetExportResponse(HttpStatusCode statusCode)
            : this(statusCode, null)
        {
        }

        public GetExportResponse(HttpStatusCode statusCode, ExportJobResult jobResult)
        {
            StatusCode = statusCode;
            JobResult = jobResult;
        }

        public HttpStatusCode StatusCode { get; }

        public ExportJobResult JobResult { get; }
    }
}
