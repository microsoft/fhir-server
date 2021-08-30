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
        /// <param name="index">Error index in file.</param>
        /// <param name="ex">Exception</param>
        /// <returns>Error in string format.</returns>
        public string Serialize(long index, Exception ex);

        /// <summary>
        /// Serialize import error into operation output.
        /// </summary>
        /// <param name="index">Error index in file.</param>
        /// <param name="errorMessage">Error Message</param>
        /// <returns>Error in string format.</returns>
        public string Serialize(long index, string errorMessage);
    }
}
