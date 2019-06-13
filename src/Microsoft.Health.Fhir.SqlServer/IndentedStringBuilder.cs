// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

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
        private readonly List<bool> _delimitedScopes = new List<bool>();

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
        /// Appends a column name to this instance.
        /// </summary>
        /// <param name="column">The column</param>
        /// <returns>This instance</returns>
        [Obsolete("Use overload with table alias instead")] // Catch calls an raise compiler warnings.
        public IndentedStringBuilder Append(Column column)
        {
            return Append(column, null);
        }

        /// <summary>
        /// Appends a column name to this instance.
        /// </summary>
        /// <param name="column">The column</param>
        /// <param name="tableAlias">The table alias to quality the column reference with</param>
        /// <returns>This instance</returns>
        public IndentedStringBuilder Append(Column column, string tableAlias)
        {
            if (!string.IsNullOrEmpty(tableAlias))
            {
                Append(tableAlias).Append('.');
            }

            return Append(column.ToString());
        }

        /// <summary>
        /// Appends a column name to this instance.
        /// </summary>
        /// <param name="column">The column</param>
        /// <returns>This instance</returns>
        [Obsolete("Use overload with table alias instead")] // Catch calls an raise compiler warnings.
        public IndentedStringBuilder AppendLine(Column column)
        {
            return AppendLine(column, null);
        }

        /// <summary>
        /// Appends a column name to this instance.
        /// </summary>
        /// <param name="column">The column</param>
        /// <param name="tableAlias">The table alias to quality the column reference with</param>
        /// <returns>This instance</returns>
        public IndentedStringBuilder AppendLine(Column column, string tableAlias)
        {
            if (!string.IsNullOrEmpty(tableAlias))
            {
                Append(tableAlias).Append('.');
            }

            return AppendLine(column.ToString());
        }

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

        /// <summary>
        /// Similar to <see cref="StringBuilder.AppendJoin{T}(string,IEnumerable{T})"/>, but without needing to build up intermediate strings.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        /// <param name="applyDelimiter">An action called to append a delimiter between elements</param>
        /// <param name="items">The input enumerable</param>
        /// <param name="writer">A function that is invoked for each element in <paramref name="items"/></param>
        /// <returns>This instance</returns>
        internal IndentedStringBuilder AppendDelimited<T>(Action<IndentedStringBuilder> applyDelimiter, IEnumerable<T> items, Action<IndentedStringBuilder, T> writer)
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
                    applyDelimiter(this);
                }

                writer(this, item);
            }

            return this;
        }

        /// <summary>
        /// Helps with appending delimited items with a prefix, delimiter, and postfix, when the number of items to be appended is not
        /// readily known.
        /// </summary>
        /// <param name="applyPrefix">A function that is called the first time <see cref="DelimitedScope.BeginDelimitedElement"/> is called</param>
        /// <param name="applyDelimiter">A function that is called the every subsequent time <see cref="DelimitedScope.BeginDelimitedElement"/> is called</param>
        /// <param name="applyPostfix">A function that is called one the <see cref="DelimitedScope"/> is disposed.</param>
        /// <returns>A disposable scope on which to call <see cref="DelimitedScope.BeginDelimitedElement"/></returns>
        internal DelimitedScope BeginDelimitedScope(Action<IndentedStringBuilder> applyPrefix, Action<IndentedStringBuilder> applyDelimiter, Action<IndentedStringBuilder> applyPostfix)
        {
            return new DelimitedScope(this, applyPrefix, applyDelimiter, applyPostfix);
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

        internal readonly struct DelimitedScope : IDisposable
        {
            private readonly IndentedStringBuilder _sb;
            private readonly Action<IndentedStringBuilder> _applyPrefix;
            private readonly Action<IndentedStringBuilder> _applyDelimiter;
            private readonly Action<IndentedStringBuilder> _applyPostfix;
            private readonly int _index;

            public DelimitedScope(IndentedStringBuilder sb, Action<IndentedStringBuilder> applyPrefix, Action<IndentedStringBuilder> applyDelimiter, Action<IndentedStringBuilder> applyPostfix)
            {
                _sb = sb;
                _applyPrefix = applyPrefix;
                _applyDelimiter = applyDelimiter;
                _applyPostfix = applyPostfix;

                _index = _sb._delimitedScopes.Count;
                _sb._delimitedScopes.Add(false);
            }

            private bool IsStarted => _sb._delimitedScopes[_index];

            // Readonly structs cannot have property setters, so this is a method
            private void SetIsStarted(bool value) => _sb._delimitedScopes[_index] = value;

            public IndentedStringBuilder BeginDelimitedElement()
            {
                if (!IsStarted)
                {
                    _applyPrefix?.Invoke(_sb);
                    SetIsStarted(true);
                }
                else
                {
                    _applyDelimiter?.Invoke(_sb);
                }

                return _sb;
            }

            public void Dispose()
            {
                if (IsStarted)
                {
                    _applyPostfix?.Invoke(_sb);
                }

                if (_sb._delimitedScopes.Count != (_index + 1))
                {
                    throw new InvalidOperationException("Delimited scope being disposed is not at the top of the stack.");
                }

                _sb._delimitedScopes.RemoveAt(_index);
            }
        }
    }
}
