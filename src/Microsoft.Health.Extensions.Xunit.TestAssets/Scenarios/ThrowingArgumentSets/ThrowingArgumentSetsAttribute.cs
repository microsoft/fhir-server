// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingArgumentSets
{
    /// <summary>
    /// An argument set attribute whose constructor throws.
    /// </summary>
    /// <remarks>
    /// Reading an attribute runs its constructor, so a class carrying this one cannot be expanded and
    /// cannot have its declared values read back the ordinary way either. The values are still in the
    /// assembly's metadata, and the failure standing in for the lost tests has to be given them, or a
    /// leg selecting positively on a data store would match nothing and report success.
    /// </remarks>
    public sealed class ThrowingArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowingArgumentSetsAttribute"/> class, which
        /// never completes.
        /// </summary>
        /// <param name="dataStore">The data stores the class would have been expanded over.</param>
        public ThrowingArgumentSetsAttribute(AssetDataStore dataStore)
            : base(dataStore)
        {
            throw new InvalidOperationException("This argument set attribute cannot be constructed.");
        }
    }
}
