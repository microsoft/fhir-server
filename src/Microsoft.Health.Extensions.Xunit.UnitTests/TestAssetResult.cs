// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// A single test result reported by a test asset run, as recorded in the TRX report.
    /// </summary>
    /// <param name="Name">
    /// The fully qualified display name the result was published under, or <c>null</c> when the
    /// runner published a result it could not attribute to a test.
    /// </param>
    /// <param name="Outcome">The TRX outcome, for example <c>Passed</c> or <c>Failed</c>.</param>
    public sealed record TestAssetResult(string Name, string Outcome);
}
