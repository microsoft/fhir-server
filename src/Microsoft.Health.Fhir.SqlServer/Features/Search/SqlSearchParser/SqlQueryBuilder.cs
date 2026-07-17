// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// A specialized string builder for constructing SQL queries with proper indentation
    /// and support for CTEs, SELECT, FROM, JOIN, WHERE, and AND clauses.
    /// </summary>
    public class SqlQueryBuilder
    {
        private readonly StringBuilder _builder = new();
        private int _indentLevel = 0;
        private readonly string _indentString = "  "; // 2 spaces per indent level
        private bool _needsIndent = true;
        private bool _isFirstCte = true;
        private readonly Stack<CteContext> _cteStack = new();

        /// <summary>
        /// Gets the current indentation level.
        /// </summary>
        public int IndentLevel => _indentLevel;

        /// <summary>
        /// Gets the length of the current query string.
        /// </summary>
        public int Length => _builder.Length;

        /// <summary>
        /// Increases the indentation level by one.
        /// </summary>
        /// <param name="count">The number of levels to increase the indentation by.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder IncreaseIndent(int count = 1)
        {
            _indentLevel += count;
            return this;
        }

        /// <summary>
        /// Decreases the indentation level by one.
        /// </summary>
        /// <param name="count">The number of levels to decrease the indentation by.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder DecreaseIndent(int count = 1)
        {
            if (_indentLevel > 0)
            {
                _indentLevel -= count;
                if (_indentLevel < 0)
                {
                    _indentLevel = 0;
                }
            }

            return this;
        }

        /// <summary>
        /// Appends a line with the current indentation.
        /// </summary>
        /// <param name="text">The text to append.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder AppendLine(string? text = null)
        {
            if (_needsIndent && !string.IsNullOrEmpty(text))
            {
                _builder.Append(GetIndent());
            }

            if (!string.IsNullOrEmpty(text))
            {
                _builder.Append(text);
            }

            _builder.AppendLine();
            _needsIndent = true;
            return this;
        }

        /// <summary>
        /// Appends text without a line break.
        /// </summary>
        /// <param name="text">The text to append.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder Append(string text)
        {
            if (_needsIndent)
            {
                _builder.Append(GetIndent());
                _needsIndent = false;
            }

            _builder.Append(text);
            return this;
        }

        /// <summary>
        /// Begins a CTE (Common Table Expression) definition.
        /// </summary>
        /// <param name="cteName">The name of the CTE.</param>
        /// <param name="isFirstCte">True if this is the first CTE in a WITH clause.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder BeginCte(string cteName, bool? isFirstCte = null)
        {
            var context = new CteContext(cteName, _indentLevel);
            _cteStack.Push(context);

            if (isFirstCte ?? _isFirstCte)
            {
                AppendLine(";WITH");
                _isFirstCte = false;
            }
            else
            {
                AppendLine(",");
            }

            AppendLine($"{cteName} AS (");
            IncreaseIndent();

            return this;
        }

        /// <summary>
        /// Ends the current CTE definition.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder EndCte()
        {
            if (_cteStack.Count == 0)
            {
                throw new InvalidOperationException("No CTE to end. Call BeginCte first.");
            }

            DecreaseIndent();
            _cteStack.Pop();
            Append(")");

            // Don't add newline here - caller can decide if they want one
            return this;
        }

        /// <summary>
        /// Appends a SELECT clause.
        /// </summary>
        /// <param name="columns">The columns to select. If null or empty, appends just "SELECT".</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder Select(params string[] columns)
        {
            if (columns == null || columns.Length == 0)
            {
                AppendLine("SELECT");
            }
            else if (columns.Length == 1)
            {
                AppendLine($"SELECT {columns[0]}");
            }
            else
            {
                Append("SELECT ");
                for (int i = 0; i < columns.Length; i++)
                {
                    if (i < columns.Length - 1)
                    {
                        Append($"{columns[i]}, ");
                    }
                    else
                    {
                        AppendLine(columns[i]);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Appends a SELECT with modifiers (like TOP, DISTINCT).
        /// </summary>
        /// <param name="modifier">The modifier (e.g., "TOP 100", "DISTINCT").</param>
        /// <param name="columns">The columns to select.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder SelectWithModifier(string modifier, params string[] columns)
        {
            if (columns == null || columns.Length == 0)
            {
                AppendLine($"SELECT {modifier}");
            }
            else if (columns.Length == 1)
            {
                AppendLine($"SELECT {modifier} {columns[0]}");
            }
            else
            {
                Append($"SELECT {modifier} ");
                for (int i = 0; i < columns.Length; i++)
                {
                    if (i < columns.Length - 1)
                    {
                        Append($"{columns[i]}, ");
                    }
                    else
                    {
                        AppendLine(columns[i]);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Appends a FROM clause.
        /// </summary>
        /// <param name="table">The table or CTE name.</param>
        /// <param name="alias">Optional alias for the table.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder From(string table, string? alias = null)
        {
            IncreaseIndent();

            if (string.IsNullOrWhiteSpace(alias))
            {
                AppendLine($"FROM {table}");
            }
            else
            {
                AppendLine($"FROM {table} AS {alias}");
            }

            DecreaseIndent();

            return this;
        }

        /// <summary>
        /// Appends a JOIN clause.
        /// </summary>
        /// <param name="joinType">The type of join (e.g., "INNER", "LEFT", "RIGHT").</param>
        /// <param name="table">The table to join.</param>
        /// <param name="alias">Optional alias for the table.</param>
        /// <param name="condition">The ON condition for the join.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder Join(string joinType, string table, string? alias, string? condition = null)
        {
            IncreaseIndent(2);

            if (string.IsNullOrWhiteSpace(alias))
            {
                Append($"{joinType} JOIN {table}");
            }
            else
            {
                Append($"{joinType} JOIN {table} AS {alias}");
            }

            if (!string.IsNullOrWhiteSpace(condition))
            {
                AppendLine($" ON {condition}");
            }
            else
            {
                AppendLine();
            }

            DecreaseIndent(2);

            return this;
        }

        /// <summary>
        /// Appends an INNER JOIN clause.
        /// </summary>
        /// <param name="table">The table to join.</param>
        /// <param name="alias">Optional alias for the table.</param>
        /// <param name="condition">The ON condition for the join.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder InnerJoin(string table, string? alias = null, string? condition = null)
        {
            return Join("INNER", table, alias, condition);
        }

        /// <summary>
        /// Appends a LEFT JOIN clause.
        /// </summary>
        /// <param name="table">The table to join.</param>
        /// <param name="alias">Optional alias for the table.</param>
        /// <param name="condition">The ON condition for the join.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder LeftJoin(string table, string? alias = null, string? condition = null)
        {
            return Join("LEFT", table, alias, condition);
        }

        /// <summary>
        /// Appends a multi-line JOIN with ON conditions on separate lines.
        /// </summary>
        /// <param name="joinType">The type of join.</param>
        /// <param name="table">The table to join.</param>
        /// <param name="alias">Optional alias for the table.</param>
        /// <param name="conditions">Multiple ON/AND conditions.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder JoinMultiLine(string joinType, string table, string? alias, params string[] conditions)
        {
            IncreaseIndent(2);

            if (string.IsNullOrWhiteSpace(alias))
            {
                AppendLine($"{joinType} JOIN {table}");
            }
            else
            {
                AppendLine($"{joinType} JOIN {table} AS {alias}");
            }

            if (conditions != null && conditions.Length > 0)
            {
                IncreaseIndent();
                AppendLine($"ON {conditions[0]}");

                for (int i = 1; i < conditions.Length; i++)
                {
                    AppendLine($"AND {conditions[i]}");
                }

                DecreaseIndent();
            }

            DecreaseIndent(2);

            return this;
        }

        /// <summary>
        /// Appends a WHERE clause.
        /// </summary>
        /// <param name="condition">The WHERE condition.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder Where(string condition)
        {
            IncreaseIndent();
            AppendLine($"WHERE {condition}");
            DecreaseIndent();
            return this;
        }

        /// <summary>
        /// Appends an AND condition (typically used after WHERE).
        /// </summary>
        /// <param name="condition">The AND condition.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder And(string condition)
        {
            IncreaseIndent(2);
            AppendLine($"AND {condition}");
            DecreaseIndent(2);
            return this;
        }

        /// <summary>
        /// Appends an OR condition.
        /// </summary>
        /// <param name="condition">The OR condition.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder Or(string condition)
        {
            IncreaseIndent(2);
            AppendLine($"OR {condition}");
            DecreaseIndent(2);
            return this;
        }

        /// <summary>
        /// Appends an ORDER BY clause.
        /// </summary>
        /// <param name="orderBy">The ORDER BY expression.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder OrderBy(string orderBy)
        {
            AppendLine($"ORDER BY {orderBy}");
            return this;
        }

        /// <summary>
        /// Appends a GROUP BY clause.
        /// </summary>
        /// <param name="groupBy">The GROUP BY expression.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder GroupBy(string groupBy)
        {
            AppendLine($"GROUP BY {groupBy}");
            return this;
        }

        /// <summary>
        /// Appends a HAVING clause.
        /// </summary>
        /// <param name="condition">The HAVING condition.</param>
        /// <returns>This builder for chaining.</returns>
        public SqlQueryBuilder Having(string condition)
        {
            AppendLine($"HAVING {condition}");
            return this;
        }

        /// <summary>
        /// Clears the builder.
        /// </summary>
        public void Clear()
        {
            _builder.Clear();
            _indentLevel = 0;
            _needsIndent = true;
            _cteStack.Clear();
        }

        /// <summary>
        /// Returns the SQL query as a string.
        /// </summary>
        /// <returns>The complete SQL query.</returns>
        public override string ToString()
        {
            return _builder.ToString();
        }

        private string GetIndent()
        {
            return string.Concat(Enumerable.Repeat(_indentString, _indentLevel));
        }

        private class CteContext
        {
            public CteContext(string name, int indentLevel)
            {
                Name = name;
                IndentLevel = indentLevel;
            }

            public string Name { get; }

            public int IndentLevel { get; }
        }
    }
}
