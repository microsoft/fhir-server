// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault
{
    /// <summary>
    /// A second fixture argument set dimension. Only the zero value is used, because a dimension
    /// that names no flag is what makes the discoverer fall back to the class-level dimension of
    /// the same position -- a position the class does not have.
    /// </summary>
    [Flags]
    public enum AssetOtherDimension
    {
        /// <summary>
        /// No flags, so this dimension contributes no values of its own.
        /// </summary>
        None = 0,

        /// <summary>
        /// An arbitrary flag, present so the type is a usable flags enum.
        /// </summary>
        Some = 1,
    }
}
