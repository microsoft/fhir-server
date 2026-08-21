// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ExecutionContextFlow
{
    /// <summary>
    /// Carries a value from a class fixture constructor to a test method through the execution
    /// context, which is the only channel that can show whether the context the fixture was built
    /// in is still the one the tests run in.
    /// </summary>
    public static class AsyncLocalProbe
    {
        /// <summary>
        /// The value the fixture constructor writes.
        /// </summary>
        public const string ExpectedValue = "set-in-fixture-constructor";

        /// <summary>
        /// The value written by the fixture constructor. An <see cref="AsyncLocal{T}"/> lives in the
        /// execution context rather than in a field, so a test reads back what the fixture wrote
        /// only if the runner deliberately restores that context.
        /// </summary>
        public static readonly AsyncLocal<string> Value = new AsyncLocal<string>();
    }
}
