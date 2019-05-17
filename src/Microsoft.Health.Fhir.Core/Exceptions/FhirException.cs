// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public abstract class FhirException : Exception
    {
        protected FhirException(params OperationOutcomeIssue[] issues)
            : this(null, issues)
        {
        }

        protected FhirException(string message, params OperationOutcomeIssue[] issues)
            : this(message, null, issues)
        {
        }

        protected FhirException(string message, Exception innerException, params OperationOutcomeIssue[] issues)
            : base(message, innerException)
        {
            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    Issues.Add(issue);
                }
            }
        }

        public ICollection<OperationOutcomeIssue> Issues { get; } = new List<OperationOutcomeIssue>();
    }
}
