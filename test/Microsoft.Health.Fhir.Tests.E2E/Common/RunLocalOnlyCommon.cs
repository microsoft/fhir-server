// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    internal static class RunLocalOnlyCommon
    {
        internal static string SkipValue()
        {
            string environmentUrl = Environment.GetEnvironmentVariable("TestEnvironmentUrl");
            return !string.IsNullOrWhiteSpace(environmentUrl) ? "Only run on localhost" : null;
        }
    }
}
