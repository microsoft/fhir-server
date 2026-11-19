// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// Identifies a search parameter whose expression could not be evaluated.
    /// </summary>
    public class SearchParameterEvaluationException : Exception
    {
        /// <summary>
        /// Initializes an evaluation failure without including the underlying exception text in its message.
        /// </summary>
        /// <param name="searchParameterUrl">The definition URL of the search parameter being evaluated.</param>
        /// <param name="innerException">The original evaluation exception.</param>
        public SearchParameterEvaluationException(Uri searchParameterUrl, Exception innerException)
            : base(string.Format(Resources.SearchParameterEvaluationFailed, searchParameterUrl), innerException)
        {
            SearchParameterUrl = EnsureArg.IsNotNull(searchParameterUrl, nameof(searchParameterUrl));
        }

        /// <summary>
        /// Gets the definition URL of the search parameter that failed evaluation.
        /// </summary>
        public Uri SearchParameterUrl { get; }
    }
}
