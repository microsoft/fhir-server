// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Serializer for error of import operation
    /// </summary>
    public interface IImportErrorSerializer
    {
        /// <summary>
        /// Serialize import error into operation output.
        /// </summary>
        /// <param name="index">Line index in input file swith offset</param>
        /// <param name="ex">Exception</param>
        /// <param name="offset">Offset in input file to start read stream</param>
        /// <returns>Error in string format.</returns>
        public string Serialize(long index, Exception ex, long offset);

        /// <summary>
        /// Serialize import error into operation output.
        /// </summary>
        /// <param name="index">Line index in input file with offset</param>
        /// <param name="errorMessage">Error Message</param>
        /// <param name="offset">Offset in input file to start read stream</param>
        /// <returns>Error in string format.</returns>
        public string Serialize(long index, string errorMessage, long offset);
    }
}
