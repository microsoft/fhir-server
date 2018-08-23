// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class TestType
    {
        public string Property1 { get; set; }

        internal static string StaticProperty { get; set; } = "Initial";

        public string CallMe()
        {
            return "hello";
        }
    }
}
