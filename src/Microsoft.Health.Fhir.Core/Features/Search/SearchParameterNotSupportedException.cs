// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// The exception that is thrown when the search parameter is not supported.
    /// </summary>
    public class SearchParameterNotSupportedException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterNotSupportedException"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="paramName">The parameter name.</param>
        public SearchParameterNotSupportedException(Type resourceType, string paramName)
        {
            Debug.Assert(resourceType != null, $"{nameof(resourceType)} should not be null.");
            Debug.Assert(!string.IsNullOrWhiteSpace(paramName), $"{nameof(paramName)} should not be null or whitespace.");

            AddIssue(string.Format(Core.Resources.SearchParameterNotSupported, paramName, resourceType.Name));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterNotSupportedException"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="paramName">The parameter name.</param>
        public SearchParameterNotSupportedException(string resourceType, string paramName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(paramName), $"{nameof(paramName)} should not be null or whitespace.");

            AddIssue(string.Format(Core.Resources.SearchParameterNotSupported, paramName, resourceType));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterNotSupportedException"/> class.
        /// </summary>
        /// <param name="definitionUri">The search parameter definition URL.</param>
        public SearchParameterNotSupportedException(Uri definitionUri)
        {
            EnsureArg.IsNotNull(definitionUri, nameof(definitionUri));

            AddIssue(string.Format(Core.Resources.SearchParameterByDefinitionUriNotSupported, definitionUri.ToString()));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterNotSupportedException"/> class.
        /// </summary>
        /// <param name="issueMessage">The issue message.</param>
        public SearchParameterNotSupportedException(string issueMessage)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(issueMessage), $"{nameof(issueMessage)} should not be null or whitespace.");

            AddIssue(issueMessage);
        }

        private void AddIssue(string diagnostics)
        {
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Forbidden,
                diagnostics));
        }
    }
}
