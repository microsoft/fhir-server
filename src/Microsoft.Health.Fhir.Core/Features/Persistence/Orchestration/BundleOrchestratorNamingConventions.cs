// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestratorNamingConventions
    {
        public const string HttpHeaderBundleProcessingLogic = "x-bundle-processing-logic";

        public const string HttpInnerBundleRequestProcessingLogic = "x-bundle-innerrequest-processing-logic";

        public const string HttpInnerBundleRequestHeaderOperationTag = "x-bundle-innerrequest-operation-id";

        public const string HttpInnerBundleRequestHeaderBundleResourceHttpVerb = "x-bundle-innerrequest-resource-http-verb";
    }
}
