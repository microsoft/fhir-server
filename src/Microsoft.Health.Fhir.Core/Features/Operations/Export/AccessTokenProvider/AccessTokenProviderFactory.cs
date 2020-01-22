// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider
{
    public class AccessTokenProviderFactory : IAccessTokenProviderFactory
    {
        private Dictionary<string, Func<IAccessTokenProvider>> _registeredTypes;

        public AccessTokenProviderFactory(IEnumerable<Func<IAccessTokenProvider>> accessTokenProviderFactories)
        {
            EnsureArg.IsNotNull(accessTokenProviderFactories, nameof(accessTokenProviderFactories));

            _registeredTypes = accessTokenProviderFactories.ToDictionary(factory => factory().AccessTokenProviderType, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IAccessTokenProvider Create(string accessTokenProviderType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(accessTokenProviderType, nameof(accessTokenProviderType));

            if (_registeredTypes.TryGetValue(accessTokenProviderType, out Func<IAccessTokenProvider> factory))
            {
                return factory();
            }

            throw new UnsupportedAccessTokenProviderException(accessTokenProviderType);
        }

        public bool IsSupportedAccessTokenProviderType(string accessTokenProviderType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(accessTokenProviderType, nameof(accessTokenProviderType));

            return _registeredTypes.ContainsKey(accessTokenProviderType);
        }
    }
}
