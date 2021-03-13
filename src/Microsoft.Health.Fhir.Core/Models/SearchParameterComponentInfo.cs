// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class SearchParameterComponentInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterComponentInfo"/> class.
        /// </summary>
        /// <param name="definitionUrl">The definition url.</param>
        /// <param name="expression">The expression.</param>
        public SearchParameterComponentInfo(Uri definitionUrl = null, string expression = null)
        {
            DefinitionUrl = definitionUrl;
            Expression = expression;
        }

        /// <summary>
        /// Gets the definition url.
        /// </summary>
        public Uri DefinitionUrl { get; }

        /// <summary>
        /// Gets the expression.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// Gets or sets the resolved search parameter object.
        /// </summary>
        public SearchParameterInfo ResolvedSearchParameter { get; set; }
    }
}
