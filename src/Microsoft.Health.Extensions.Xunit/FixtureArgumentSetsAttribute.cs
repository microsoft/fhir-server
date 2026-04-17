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
    /// Controls how test collections are grouped for fixture argument variants.
    /// </summary>
    public enum FixtureArgumentSetCollectionBehavior
    {
        /// <summary>
        /// All test classes with the same fixture argument variant share a collection.
        /// </summary>
        SharedPerVariant,

        /// <summary>
        /// Each test class gets its own collection for a given fixture argument variant.
        /// </summary>
        PerClass,
    }

    /// <summary>
    /// Derive from this attribute to declare combinations of argument values that a class fixture's constructor should be called with.
    /// Arguments are required to be flags enums.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public abstract class FixtureArgumentSetsAttribute : Attribute
    {
        private readonly Enum[] _argumentSets;

        protected FixtureArgumentSetsAttribute(params Enum[] argumentSets)
        {
            EnsureArg.IsNotNull(argumentSets, nameof(argumentSets));
            _argumentSets = argumentSets;
            CollectionBehavior = FixtureArgumentSetCollectionBehavior.SharedPerVariant;
        }

        /// <summary>
        /// Gets the collection grouping behavior for fixture argument variants.
        /// </summary>
        public FixtureArgumentSetCollectionBehavior CollectionBehavior { get; protected set; }

        internal IReadOnlyList<Enum> GetArgumentSets()
        {
            return _argumentSets;
        }
    }
}
