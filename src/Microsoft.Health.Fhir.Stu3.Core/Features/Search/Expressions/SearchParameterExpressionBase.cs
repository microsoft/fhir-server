// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression over a search parameter.
    /// </summary>
    public abstract class SearchParameterExpressionBase : Expression
    {
        protected SearchParameterExpressionBase(SearchParameter searchParameter)
        {
            Parameter = searchParameter;
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        }

        public SearchParameter Parameter { get; }
    }
}
