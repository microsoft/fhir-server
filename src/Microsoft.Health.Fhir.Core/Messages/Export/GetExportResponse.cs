// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Features.Export;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class GetExportResponse
    {
        public GetExportResponse(bool jobExists, HttpStatusCode statusCode)
            : this(jobExists, statusCode, null)
        {
        }

        public GetExportResponse(bool jobExists, HttpStatusCode statusCode, ExportJobResult jobResult)
        {
            JobExists = jobExists;
            StatusCode = statusCode;
            JobResult = jobResult;
        }

        public bool JobExists { get; }

        public HttpStatusCode StatusCode { get; }

        public ExportJobResult JobResult { get; }
    }
}
