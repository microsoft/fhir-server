// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingTrait
{
    /// <summary>
    /// A trait attribute that throws when asked for its traits, standing in for one that computes a
    /// trait from configuration that is not present.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ThrowingTraitAttribute : Attribute, ITraitAttribute
    {
        /// <summary>
        /// Throws, as a trait attribute computing its value at discovery time may.
        /// </summary>
        /// <returns>Never returns.</returns>
        public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
        {
            throw new InvalidOperationException("ASSET: this trait attribute cannot produce its traits");
        }
    }
}
