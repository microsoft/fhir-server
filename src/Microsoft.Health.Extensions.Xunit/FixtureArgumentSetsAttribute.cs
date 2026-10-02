// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Derive from this attribute to declare combinations of argument values that a class fixture's constructor should be called with.
    /// Arguments are required to be flags enums.
    /// </summary>
    /// <remarks>
    /// Unlike xUnit v2, xUnit v3 discovery does not expose an attribute's constructor arguments through a reflection
    /// abstraction, so the concrete argument values are captured here and surfaced through <see cref="GetArgumentSets"/>.
    /// Each returned <see cref="Enum"/> is one dimension of the variant; a <c>[Flags]</c> value with several bits set
    /// expands into one variant per single bit.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public abstract class FixtureArgumentSetsAttribute : Attribute
    {
        private readonly Enum[] _argumentSets;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureArgumentSetsAttribute"/> class.
        /// </summary>
        /// <param name="argumentSets">One flags enum per fixture argument dimension.</param>
        protected FixtureArgumentSetsAttribute(params Enum[] argumentSets)
        {
            EnsureArg.IsNotNull(argumentSets, nameof(argumentSets));
            _argumentSets = argumentSets;
        }

        /// <summary>
        /// Gets the declared argument dimensions, one flags enum per dimension.
        /// </summary>
        /// <returns>The argument sets supplied to the attribute constructor.</returns>
        public Enum[] GetArgumentSets() => _argumentSets;
    }
}
