// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class TestTypeWithArgs
    {
        public TestTypeWithArgs(TestType oneArg)
            : this(oneArg, null)
        {
        }

        public TestTypeWithArgs(TestType oneArg, string secondArg)
        {
            OneArg = oneArg;
            SecondArg = secondArg;
        }

        public TestType OneArg { get; }

        public string SecondArg { get; }
    }
}
