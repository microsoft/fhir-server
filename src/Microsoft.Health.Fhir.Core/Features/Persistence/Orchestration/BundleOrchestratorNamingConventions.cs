// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestratorNamingConventions
    {
        // Public constants for HTTP headers used in bundle processing (parallel or sequential).
        public const string HttpHeaderBundleProcessingLogic = "x-bundle-processing-logic";

        // Internal constant for HTTP header used to pass the bundle context to internal components.
        public const string HttpBundleInnerRequestExecutionContext = "x-bundle-internal-request-execution-context";
    }
}
