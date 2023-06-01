// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Parser for raw data into ImportResource.
    /// </summary>
    public interface IImportResourceParser
    {
        /// <summary>
        /// Parse raw resource data.
        /// </summary>
        /// <param name="index">index of the resource.</param>
        /// <param name="offset">Read stream offset in blob/file.</param>
        /// <param name="length">Raw resource Json length in bytes including EOL</param>
        /// <param name="rawResource">raw content in string format.</param>
        /// <param name="importMode">import mode.</param>
        /// <returns>ImportResource</returns>
        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode);
    }
}
