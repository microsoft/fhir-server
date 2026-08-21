// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace Xunit
{
    /// <summary>
    /// Compatibility attribute for legacy SkippableTheory usage. In xUnit v3 a dynamically skipped test is expressed
    /// through Assert.Skip rather than a separate attribute, so this is a source-compatibility alias for the plain
    /// theory attribute and exists only so existing call sites keep compiling.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SkippableTheoryAttribute : TheoryAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SkippableTheoryAttribute"/> class.
        /// </summary>
        /// <param name="sourceFilePath">The source file containing the test method. Supplied by the compiler.</param>
        /// <param name="sourceLineNumber">The line number of the test method. Supplied by the compiler.</param>
        public SkippableTheoryAttribute([CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        {
        }
    }
}
