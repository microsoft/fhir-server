// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public abstract class FhirException : Exception
    {
        protected FhirException(params OperationOutcome.IssueComponent[] issues)
            : this(null, issues)
        {
        }

        protected FhirException(string message, params OperationOutcome.IssueComponent[] issues)
            : this(message, null, issues)
        {
        }

        protected FhirException(string message, Exception innerException, params OperationOutcome.IssueComponent[] issues)
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

        public ICollection<OperationOutcome.IssueComponent> Issues { get; } = new List<OperationOutcome.IssueComponent>();
    }
}
