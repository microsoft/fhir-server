// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public static class KnownFhirPaths
    {
        public const string BundleEntries = "Resource.entry.resource";

        public const string BundleNextLink = "Resource.link.where(relation = 'next').url";

        public const string BundleSelfLink = "Resource.link.where(relation = 'self').url";

        public const string ResourceNarrative = "text.div";
    }
}
