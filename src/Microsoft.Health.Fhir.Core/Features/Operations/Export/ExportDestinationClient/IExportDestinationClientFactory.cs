// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    /// <summary>
    /// Provides functionality to create an instance of <see cref="IExportDestinationClient"/> based on destination type.
    /// </summary>
    public interface IExportDestinationClientFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IExportDestinationClient"/> based on <paramref name="destinationType"/>.
        /// </summary>
        /// <param name="destinationType">The requested destination type.</param>
        /// <returns>An instance of <see cref="IExportDestinationClient"/>.</returns>
        /// <exception cref="UnsupportedDestinationTypeException">Thrown when the <paramref name="destinationType"/> is not supported.</exception>
        IExportDestinationClient Create(string destinationType);

        /// <summary>
        /// Checks whether the <paramref name="destinationType"/> is supported or not.
        /// </summary>
        /// <param name="destinationType">The requested destination type.</param>
        /// <returns><c>true</c> if the <paramref name="destinationType"/> is supported; otherwise, <c>false</c>.</returns>
        bool IsSupportedDestinationType(string destinationType);
    }
}
