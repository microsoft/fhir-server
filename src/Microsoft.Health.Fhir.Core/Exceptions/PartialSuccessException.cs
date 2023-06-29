// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class PartialSuccessException<T> : Exception
    {
        public PartialSuccessException(Exception innerException, T paritalResults)
            : base(innerException.Message, innerException)
        {
            PartialResults = paritalResults;
        }

        public T PartialResults { get; private set; }
    }
}
