// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.CodeDom.Compiler;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer
{
    /// <summary>
    /// A wrapper around <see cref="StringBuilder"/> that provides methods that insert a indent string and track the current indentation level.
    /// Inspired by <see cref="IndentedTextWriter"/>
    /// </summary>
    internal partial class IndentedStringBuilder
    {
        private const char IndentChar = ' ';
        private const int CharCountPerIndent = 4;

        private bool _indentPending;
        private int _indentLevel;

        internal int IndentLevel
        {
            get => _indentLevel;
            set => _indentLevel = EnsureArg.IsGte(value, 0);
        }

        /// <summary>
        /// Increments the indent level until the returned object is disposed.
        /// </summary>
        /// <returns>A scope to be disposed when the indentation level is to be restored</returns>
        internal IndentedScope Indent() => new IndentedScope(this);

        protected virtual void AppendIndent()
        {
            if (_indentPending)
            {
                _inner.Append(IndentChar, IndentLevel * CharCountPerIndent);
                _indentPending = false;
            }
        }

        internal struct IndentedScope : IDisposable
        {
            private readonly IndentedStringBuilder _sb;

            public IndentedScope(IndentedStringBuilder sb)
            {
                _sb = sb;
                _sb.IndentLevel++;
            }

            public void Dispose()
            {
                _sb.IndentLevel--;
            }
        }
    }
}
