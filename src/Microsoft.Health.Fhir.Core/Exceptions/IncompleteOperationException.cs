// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Abstractions.Exceptions;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// An exception that is thrown when an operation has not run to completion.
    /// </summary>
    /// <typeparam name="T">The type of the partial results, if any</typeparam>
    public class IncompleteOperationException<T> : MicrosoftHealthException
    {
        public IncompleteOperationException(Exception innerException, T partialResults)
            : base(innerException.Message, innerException)
        {
            PartialResults = partialResults;
        }

        public T PartialResults { get; private set; }
    }
}
