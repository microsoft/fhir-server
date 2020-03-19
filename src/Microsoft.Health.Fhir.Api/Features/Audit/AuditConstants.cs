// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public static class AuditConstants
    {
        public const string CustomAuditHeaderKeyValue = "CustomAuditHeaderCollectionKeyValue";

        public const int MaximumNumberOfCustomHeaders = 10;

        public const int MaximumLengthOfCustomHeader = 2048;
    }
}
