// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of a bulk import operation.
    /// </summary>
    public class ImportBundleResult : ResourceActionResult<ImportJobResult>
    {
        public ImportBundleResult(int count, HttpStatusCode statusCode)
            : base(null, statusCode)
        {
            LoadedResources = count;
        }

        public int LoadedResources { get; }
    }
}
