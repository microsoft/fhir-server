// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.InMemory
{
    internal class ComparisonValueVisitor : ISearchValueVisitor
    {
        private readonly BinaryOperator _expressionBinaryOperator;
        private readonly IComparable _second;

        private readonly List<Func<bool>> _comparisonValues = new List<Func<bool>>();

        public ComparisonValueVisitor(BinaryOperator expressionBinaryOperator, IComparable second)
        {
            _expressionBinaryOperator = expressionBinaryOperator;
            _second = second;
        }

        public void Visit(CompositeSearchValue composite)
        {
            foreach (IReadOnlyList<ISearchValue> c in composite.Components)
            {
                foreach (ISearchValue inner in c)
                {
                    inner.AcceptVisitor(this);
                }
            }
        }

        public void Visit(DateTimeSearchValue dateTime)
        {
            AddComparison(_expressionBinaryOperator, dateTime.Start);
        }

        public void Visit(NumberSearchValue number)
        {
            AddComparison(_expressionBinaryOperator, number.High);
        }

        public void Visit(QuantitySearchValue quantity)
        {
            AddComparison(_expressionBinaryOperator, quantity.High);
        }

        public void Visit(ReferenceSearchValue reference)
        {
            AddComparison(_expressionBinaryOperator, reference.ResourceId);
        }

        public void Visit(StringSearchValue s)
        {
            AddComparison(_expressionBinaryOperator, s.String);
        }

        public void Visit(TokenSearchValue token)
        {
            AddComparison(_expressionBinaryOperator, token.Text, token.System, token.Code);
        }

        public void Visit(UriSearchValue uri)
        {
            AddComparison(_expressionBinaryOperator, uri.Uri);
        }

        private void AddComparison(BinaryOperator binaryOperator, params IComparable[] first)
        {
            switch (binaryOperator)
            {
                case BinaryOperator.Equal:
                    _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) == 0));
                    break;
                case BinaryOperator.GreaterThan:
                    _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) > 0));
                    break;
                case BinaryOperator.LessThan:
                    _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) < 0));
                    break;
                case BinaryOperator.NotEqual:
                    _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) != 0));
                    break;
                case BinaryOperator.GreaterThanOrEqual:
                    _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) >= 0));
                    break;
                case BinaryOperator.LessThanOrEqual:
                    _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) <= 0));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(binaryOperator));
            }
        }

        public bool Compare()
        {
            return _comparisonValues.All(x => x.Invoke());
        }
    }
}
