// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace Xunit
{
    /// <summary>
    /// Compatibility attribute for legacy SkippableTheory usage (temporary shim to reduce PR review churn).
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
