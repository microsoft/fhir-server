// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
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
        private readonly Stack<DelimitedScope> _delimitedScopes = new Stack<DelimitedScope>();

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

        /// <summary>
        /// Similar to <see cref="StringBuilder.AppendJoin{T}(string,IEnumerable{T})"/>, but without needing to build up intermediate strings.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        /// <param name="delimiter">The delimiter to place between elements</param>
        /// <param name="items">The input enumerable</param>
        /// <param name="writer">A function that is invoked for each element in <paramref name="items"/></param>
        /// <returns>This instance</returns>
        internal IndentedStringBuilder AppendDelimited<T>(string delimiter, IEnumerable<T> items, Action<IndentedStringBuilder, T> writer)
        {
            bool first = true;
            foreach (T item in items)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    Append(delimiter);
                }

                writer(this, item);
            }

            return this;
        }

        internal DelimitedScope Delimit(Action<IndentedStringBuilder> applyPrefix, Action<IndentedStringBuilder> applyDelimiter, Action<IndentedStringBuilder> applyPostfix)
        {
            var delimitedScope = new DelimitedScope(this, applyPrefix, applyDelimiter, applyPostfix);
            _delimitedScopes.Push(delimitedScope);
            return delimitedScope;
        }

        internal IndentedStringBuilder BeginDelimitedElement()
        {
            if (_delimitedScopes.TryPop(out var scope))
            {
                scope.BeginDelimited();
            }

            throw new InvalidOperationException("Delimited scope stack is empty");
        }

        private void AppendIndent()
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

        internal struct DelimitedScope : IDisposable
        {
            private readonly IndentedStringBuilder _sb;
            private readonly Action<IndentedStringBuilder> _applyPrefix;
            private readonly Action<IndentedStringBuilder> _applyDelimiter;
            private readonly Action<IndentedStringBuilder> _applyPostfix;
            private bool _started;

            public DelimitedScope(IndentedStringBuilder sb, Action<IndentedStringBuilder> applyPrefix, Action<IndentedStringBuilder> applyDelimiter, Action<IndentedStringBuilder> applyPostfix)
            {
                _sb = sb;
                _applyPrefix = applyPrefix;
                _applyDelimiter = applyDelimiter;
                _applyPostfix = applyPostfix;
                _started = false;
            }

            public void BeginDelimited()
            {
                if (!_started)
                {
                    _applyPrefix?.Invoke(_sb);
                    _started = true;
                }
                else
                {
                    _applyDelimiter?.Invoke(_sb);
                }
            }

            public void Dispose()
            {
                if (_started)
                {
                    _applyPostfix?.Invoke(_sb);
                }
            }
        }
    }
}
