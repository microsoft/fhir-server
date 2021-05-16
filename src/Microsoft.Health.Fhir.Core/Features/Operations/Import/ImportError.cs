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

        /// <summary>
        /// Sequence ID for resource
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Index in the resource file.
        /// </summary>
        public long Index { get; set; }

        /// <summary>
        /// Exception during processing data.
        /// </summary>
        public Exception Exception { get; set; }
    }
}
