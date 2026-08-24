// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancellationLookalikeFault
{
    /// <summary>
    /// An argument set attribute whose constructor throws an exception shaped like cancellation.
    /// </summary>
    /// <remarks>
    /// <see cref="TaskCanceledException"/> is what anything that awaits with a timeout throws, so an
    /// attribute that reaches out to something while being constructed can raise it without the run
    /// having been cancelled at all. Discovery is not handed a cancellation token, so the exception's
    /// type is the only thing that distinguishes this from a real Ctrl+C - which is why the type
    /// alone must not be allowed to decide. Taken for cancellation, this is rethrown, the class is
    /// dropped, and the run ends green with its tests missing.
    /// </remarks>
    public sealed class CancellationLookalikeArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CancellationLookalikeArgumentSetsAttribute"/> class, which never completes.
        /// </summary>
        /// <param name="dataStore">The data stores the class would have been expanded over.</param>
        public CancellationLookalikeArgumentSetsAttribute(AssetDataStore dataStore)
            : base(dataStore)
        {
            throw new TaskCanceledException("This argument set attribute gave up waiting.");
        }
    }
}
