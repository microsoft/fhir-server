// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportError
    {
        public ImportError(long id, long index, Exception exception)
        {
            Id = id;
            Index = index;
            Exception = exception;
        }

        public long Id { get; set; }

        public long Index { get; set; }

        public Exception Exception { get; set; }
    }
}
