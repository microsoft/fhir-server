// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.Common.FixtureParameters
{
    [Flags]
    public enum Format
    {
        Json = 1,

        Xml = 2,

        All = Json | Xml,
    }
}
