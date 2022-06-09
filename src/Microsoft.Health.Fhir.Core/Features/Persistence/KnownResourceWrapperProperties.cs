// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class KnownResourceWrapperProperties
    {
        public const string LastModified = "lmd";

        public const string RawResource = "raw";

        public const string IsDeleted = "isd";

        public const string IsHistory = "ish";

        public const string ResourceId = "rid";

        public const string ResourceTypeName = "rtn";

        public const string Request = "rq";

        public const string Version = "ver";

        public const string SearchIndices = "sind";

        public const string LastModifiedClaims = "lmcls";

        public const string CompartmentIndices = "cind";

        public const string Device = "dev";

        public const string Encounter = "enc";

        public const string Patient = "pat";

        public const string Practitioner = "pract";

        public const string RelatedPerson = "relp";

        public const string SearchParameterHash = "shash";

        public const string RawResourceContainsVersion = "rawcver";

        public const string RawResourceContainsLastUpdatedTime = "rawclu";
    }
}
