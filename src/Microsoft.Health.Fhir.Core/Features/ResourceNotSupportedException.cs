// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features
{
    /// <summary>
    /// The exception that is thrown when the resource is not supported.
    /// </summary>
    public class ResourceNotSupportedException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceNotSupportedException"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        public ResourceNotSupportedException(Type resourceType)
            : this(resourceType?.Name)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceNotSupportedException"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        public ResourceNotSupportedException(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            Issues.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Forbidden,
                Diagnostics = string.Format(Core.Resources.ResourceNotSupported, resourceType),
            });
        }
    }
}
