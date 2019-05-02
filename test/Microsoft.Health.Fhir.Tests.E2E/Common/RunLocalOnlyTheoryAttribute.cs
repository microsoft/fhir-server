// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public sealed class RunLocalOnlyTheoryAttribute : TheoryAttribute
    {
        public RunLocalOnlyTheoryAttribute()
        {
            Skip = RunLocalOnlyCommon.SkipValue();
        }
    }
}
