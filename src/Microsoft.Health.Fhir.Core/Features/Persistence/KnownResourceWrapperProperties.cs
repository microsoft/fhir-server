// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class KnownResourceWrapperProperties
    {
        public const string LastModified = "lMod";

        public const string RawResource = "raw";

        public const string IsDeleted = "isD";

        public const string IsHistory = "isH";

        public const string ResourceId = "rId";

        public const string ResourceTypeName = "rT";

        public const string Request = "req";

        public const string Version = "ver";

        public const string SearchIndices = "sInd";

        public const string LastModifiedClaims = "lMCls";

        public const string CompartmentIndices = "cInd";

        public const string Device = "dev";

        public const string Encounter = "enc";

        public const string Patient = "pat";

        public const string Practitioner = "pract";

        public const string RelatedPerson = "relP";

        public const string SearchParameterHash = "sHash";

        public const string RawResourceContainsVersion = "rawContVer";

        public const string RawResourceContainsLastUpdatedTime = "rawContLU";
    }
}
