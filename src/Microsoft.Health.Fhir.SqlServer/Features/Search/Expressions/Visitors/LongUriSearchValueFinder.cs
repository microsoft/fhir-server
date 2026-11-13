// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Finds URI search values that are at least as long as the URI search index column.
    /// The index stores and compares only the first <see cref="IndexedUriMaxLength"/> characters, so such
    /// values can also match resources whose URI shares that prefix.
    /// Returns the codes of the affected search parameters, or <c>null</c> if there are none.
    /// </summary>
    internal sealed class LongUriSearchValueFinder : DefaultExpressionVisitor<SearchParameterInfo, IReadOnlyCollection<string>>
    {
        internal static readonly LongUriSearchValueFinder Instance = new LongUriSearchValueFinder();

        private LongUriSearchValueFinder()
            : base(Merge)
        {
        }

        /// <summary>
        /// Gets the number of URI characters stored in, and compared by, the URI search index.
        /// </summary>
        internal static int IndexedUriMaxLength { get; } = (int)VLatest.UriSearchParam.Uri.Metadata.MaxLength;

        public override IReadOnlyCollection<string> VisitSearchParameter(SearchParameterExpression expression, SearchParameterInfo context)
        {
            return expression.Expression.AcceptVisitor(this, expression.Parameter);
        }

        public override IReadOnlyCollection<string> VisitString(StringExpression expression, SearchParameterInfo context)
        {
            if (expression.FieldName == FieldName.Uri && expression.Value?.Length >= IndexedUriMaxLength)
            {
                return new[] { context?.Code ?? nameof(FieldName.Uri) };
            }

            return null;
        }

        private static IReadOnlyCollection<string> Merge(IReadOnlyCollection<string> accumulated, IReadOnlyCollection<string> current)
        {
            if (accumulated == null)
            {
                return current;
            }

            if (current == null)
            {
                return accumulated;
            }

            return accumulated.Union(current).ToList();
        }
    }
}
