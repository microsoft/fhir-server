// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public abstract class SearchParameterExpressionBase : Expression
    {
        protected SearchParameterExpressionBase(string searchParameterName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(searchParameterName, nameof(searchParameterName));
            SearchParameterName = searchParameterName;
        }

        public string SearchParameterName { get; }
    }
}
