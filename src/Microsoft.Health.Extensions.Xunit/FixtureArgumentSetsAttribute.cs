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
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public abstract class FixtureArgumentSetsAttribute : Attribute
    {
        protected FixtureArgumentSetsAttribute(params Enum[] argumentSets)
        {
            EnsureArg.IsNotNull(argumentSets, nameof(argumentSets));
        }
    }
}
