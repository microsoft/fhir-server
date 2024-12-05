// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public sealed class PartitionKeyExceededStorageException : FhirException
    {
        public PartitionKeyExceededStorageException(string message)
            : base(message)
        {
            Init(message);
        }

        public PartitionKeyExceededStorageException(string message, Exception innerException)
            : base(message, innerException)
        {
            Init(message);
        }

        private void Init(string message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Forbidden, // Returning forbidden as the data source.
                    message));
        }
    }
}
