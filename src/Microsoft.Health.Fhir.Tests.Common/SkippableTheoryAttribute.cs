// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Compatibility shim for Xunit.SkippableTheory migration to xUnit v3.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SkippableTheoryAttribute : TheoryAttribute
    {
        public SkippableTheoryAttribute(
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        {
        }
    }
}
