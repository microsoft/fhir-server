// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Shared constants for dynamically skipping tests via <c>Assert.SkipWhen</c>/<c>Assert.SkipUnless</c>.
    /// </summary>
    public static class SkipReasons
    {
        /// <summary>
        /// The neutral reason used where a skip condition carried no message under the former
        /// <c>Xunit.SkippableFact</c> API (which allowed a reason-less <c>Skip.If</c>/<c>Skip.IfNot</c>).
        /// </summary>
        public const string Unspecified = "Conditionally skipped.";
    }
}
