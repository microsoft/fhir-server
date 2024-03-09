// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Azure.Core;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of a import bundle operation.
    /// </summary>
    public class ImportBundleActionResult : ResourceActionResult<ImportBundleResult>
    {
        public ImportBundleActionResult(ImportBundleResult importBundleResult, HttpStatusCode statusCode)
            : base(importBundleResult, statusCode)
        {
        }

        public ImportBundleResult ImportBundleResult { get; private set; }
    }
}
