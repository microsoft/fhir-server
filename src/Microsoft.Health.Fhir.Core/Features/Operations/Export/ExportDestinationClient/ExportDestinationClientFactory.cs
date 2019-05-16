// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    /// <summary>
    /// Provides functionality to create an instance of <see cref="IExportDestinationClient"/> based on destination type.
    /// </summary>
    public class ExportDestinationClientFactory : IExportDestinationClientFactory
    {
        private Dictionary<string, Func<IExportDestinationClient>> _registeredTypes;

        public ExportDestinationClientFactory(IEnumerable<Func<IExportDestinationClient>> destinationClientFactories)
        {
            EnsureArg.IsNotNull(destinationClientFactories, nameof(destinationClientFactories));

            _registeredTypes = destinationClientFactories.ToDictionary(factory => factory().DestinationType, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IExportDestinationClient Create(string destinationType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(destinationType, nameof(destinationType));

            if (_registeredTypes.TryGetValue(destinationType, out Func<IExportDestinationClient> factory))
            {
                return factory();
            }

            throw new UnsupportedDestinationTypeException(destinationType);
        }

        /// <inheritdoc />
        public bool IsSupportedDestinationType(string destinationType)
        {
            return _registeredTypes.TryGetValue(destinationType, out Func<IExportDestinationClient> _);
        }
    }
}
