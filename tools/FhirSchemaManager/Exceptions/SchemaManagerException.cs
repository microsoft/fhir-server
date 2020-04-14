// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;

namespace FhirSchemaManager.Exceptions
{
    public class SchemaManagerException : Exception
    {
        public SchemaManagerException(string message)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");
        }
    }
}
