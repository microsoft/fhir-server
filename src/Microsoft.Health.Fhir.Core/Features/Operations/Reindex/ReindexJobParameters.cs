// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    /// <summary>
    /// List of parameters users can specify when creating a reindex job
    /// </summary>
    public static class ReindexJobParameters
    {
        public const string MaximumConcurrency = "maximumConcurrency";

        public const string Scope = "scope";

        public const string Status = "status";

        public const string Id = "id";

        public const string StartTime = "startTime";

        public const string Progress = "progress";
    }
}
