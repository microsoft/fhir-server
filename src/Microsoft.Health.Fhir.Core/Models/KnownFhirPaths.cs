// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public static class KnownFhirPaths
    {
        /// <summary>
        /// The extension URL used to mark a resource's meta as soft-deleted.
        /// </summary>
        public const string AzureSoftDeletedExtensionUrl = "http://azurehealthcareapis.com/data-extensions/deleted-state";

        public const string BundleEntries = "Resource.entry.resource";

        public const string BundleNextLink = "Resource.link.where(relation = 'next').url";

        public const string BundleSelfLink = "Resource.link.where(relation = 'self').url";

        public const string BundleType = "Resource.type";

        /// <summary>
        /// The FHIRPath expression for a resource narrative. The <c>div</c> identifier is escaped because it is a FHIRPath keyword.
        /// </summary>
        public const string ResourceNarrative = "text.`div`";

        /// <summary>
        /// The unescaped display path for a resource narrative. This is for display only and is not the equivalent FHIRPath expression.
        /// </summary>
        public const string ResourceNarrativeDisplayPath = "text.div";

        public const string IsSoftDeletedExtension = $"Resource.meta.extension.where(url = '{AzureSoftDeletedExtensionUrl}').where(value='soft-deleted').exists()";
    }
}
