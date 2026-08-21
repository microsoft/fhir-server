// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ExecutionContextFlow
{
    /// <summary>
    /// A class fixture that writes to an <see cref="System.Threading.AsyncLocal{T}"/> from its
    /// constructor, the way <c>FhirStorageTestsFixture</c> builds its <c>ResourceIdProvider</c>
    /// there rather than in <c>InitializeAsync</c>.
    /// </summary>
    public sealed class AsyncLocalWritingFixture
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncLocalWritingFixture"/> class, writing
        /// the probe value into the execution context that is current while the fixture is built.
        /// </summary>
        public AsyncLocalWritingFixture()
        {
            AsyncLocalProbe.Value.Value = AsyncLocalProbe.ExpectedValue;
        }
    }
}
