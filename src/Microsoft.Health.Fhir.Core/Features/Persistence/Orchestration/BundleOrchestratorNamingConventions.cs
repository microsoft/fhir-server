// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestratorNamingConventions
    {
        public const string HttpHeaderBundleProcessingLogic = "x-bundle-processing-logic";

        public const string HttpHeaderOperationTag = "x-bundle-operation-id";
    }
}
