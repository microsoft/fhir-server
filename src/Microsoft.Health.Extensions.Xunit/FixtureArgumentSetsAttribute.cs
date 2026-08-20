// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Derive from this attribute to declare combinations of argument values that a class fixture's constructor should be called with.
    /// Arguments are required to be flags enums.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public abstract class FixtureArgumentSetsAttribute : Attribute
    {
        private readonly Enum[] _argumentSets;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureArgumentSetsAttribute"/> class.
        /// </summary>
        /// <param name="argumentSets">
        /// The flags enum values to pass to the class fixture's constructor. Each distinct enum type
        /// contributes one argument, and the test class is expanded into one variant per combination
        /// of the flags set within each type.
        /// </param>
        protected FixtureArgumentSetsAttribute(params Enum[] argumentSets)
        {
            EnsureArg.IsNotNull(argumentSets, nameof(argumentSets));
            _argumentSets = argumentSets;
        }

        internal IReadOnlyList<Enum> GetArgumentSets()
        {
            return _argumentSets;
        }
    }
}
