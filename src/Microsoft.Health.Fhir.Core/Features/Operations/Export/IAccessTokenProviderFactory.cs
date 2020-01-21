// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public interface IAccessTokenProviderFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IAccessTokenProvider"/> based on <paramref name="destinationType"/>.
        /// </summary>
        /// <param name="destinationType">The requested destination type.</param>
        /// <returns>An instance of <see cref="IAccessTokenProvider"/>.</returns>
        /// <exception cref="UnsupportedDestinationTypeException">Thrown when the <paramref name="destinationType"/> is not supported.</exception>
        IAccessTokenProvider Create(string destinationType);
    }
}
