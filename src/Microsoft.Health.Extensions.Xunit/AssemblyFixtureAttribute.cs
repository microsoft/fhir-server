// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Placed on an assembly to indicate that the given type should be instantiated
    /// before any tests are executed and disposed (if it implements IDisposable)
    /// at the end of the test run.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class AssemblyFixtureAttribute : Attribute
    {
        public AssemblyFixtureAttribute(Type fixtureType)
        {
            EnsureArg.IsNotNull(fixtureType, nameof(fixtureType));
            FixtureType = fixtureType;
        }

        public Type FixtureType { get; }
    }
}
