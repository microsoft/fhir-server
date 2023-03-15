// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class KnownResourceWrapperProperties
    {
        public const string ResourceSurrogateId = "resourceSurrogateId";

        public const string LastModified = "lastModified";

        public const string RawResource = "rawResource";

        public const string IsDeleted = "isDeleted";

        public const string IsHistory = "isHistory";

        public const string ResourceId = "resourceId";

        public const string ResourceTypeName = "resourceTypeName";

        public const string Request = "request";

        public const string Version = "version";

        public const string SearchIndices = "searchIndices";

        public const string LastModifiedClaims = "lastModifiedClaims";

        public const string CompartmentIndices = "compartmentIndices";

        public const string Device = "device";

        public const string Encounter = "encounter";

        public const string Patient = "patient";

        public const string Practitioner = "practitioner";

        public const string RelatedPerson = "relatedPerson";

        public const string SearchParameterHash = "searchParameterHash";

        public const string RawResourceContainsVersion = "rawResourceContainsVersion";

        public const string RawResourceContainsLastUpdatedTime = "rawResourceContainsLastUpdatedTime";
    }
}
