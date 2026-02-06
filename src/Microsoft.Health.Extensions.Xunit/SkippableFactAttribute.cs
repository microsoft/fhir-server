// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace Xunit
{
    /// <summary>
    /// Compatibility attribute for legacy SkippableFact usage.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SkippableFactAttribute : FactAttribute
    {
        public SkippableFactAttribute([CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            SourceFilePath = sourceFilePath;
            SourceLineNumber = sourceLineNumber;
        }

        internal new string SourceFilePath { get; }

        internal new int SourceLineNumber { get; }
    }
}
