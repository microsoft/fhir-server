// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportFileEtagNotMatchException : Exception
    {
        public ImportFileEtagNotMatchException(string message)
            : base(message, null)
        {
        }

        public ImportFileEtagNotMatchException(string message, Exception innerException)
            : base(message, innerException)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");
        }
    }
}
