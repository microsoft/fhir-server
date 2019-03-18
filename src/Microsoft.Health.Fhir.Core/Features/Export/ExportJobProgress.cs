// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobProgress
    {
        public ExportJobProgress(string query, int page)
        {
            EnsureArg.IsNotNullOrEmpty(query, nameof(query));

            Query = query;
            Page = page;
        }

        public string Query { get; }

        public int Page { get; }
    }
}
