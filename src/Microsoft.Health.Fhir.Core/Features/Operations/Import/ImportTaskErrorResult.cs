// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportTaskErrorResult
    {
        public HttpStatusCode HttpStatusCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
