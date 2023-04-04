// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Exception thrown when a filtering criteria fails.
    /// </summary>
    public class FilterCriteriaException : FhirException
    {
        public FilterCriteriaException(string message, Exception exception)
            : base(message, exception)
        {
        }
    }
}
