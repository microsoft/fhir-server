// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.Common.FixtureParameters
{
    [Flags]
    public enum FhirVersion
    {
        Stu3 = 1,

        R4 = 2,

        All = Stu3 | R4,
    }
}
