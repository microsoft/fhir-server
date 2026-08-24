// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingAttributeConstructor
{
    /// <summary>
    /// An ordinary attribute - not a trait attribute - whose constructor throws.
    /// </summary>
    /// <remarks>
    /// Reading a declaration's attributes constructs all of them, so this one is enough to make that
    /// read fail for the declaration it sits on, taking the sound trait attributes beside it with it.
    /// It deliberately is not an <c>ITraitAttribute</c>: the traits are lost because of where the
    /// attribute sits, not because of what it is.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ThrowingConstructorAttribute : Attribute
    {
        public ThrowingConstructorAttribute()
        {
            throw new InvalidOperationException("This attribute's constructor always throws.");
        }
    }
}
