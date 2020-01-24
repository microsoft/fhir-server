// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider
{
    public class UnsupportedAccessTokenProviderException : Exception
    {
        public UnsupportedAccessTokenProviderException(string accessTokenProviderType)
            : base(string.Format(Resources.UnsupportedAccessTokenProvider, accessTokenProviderType))
        {
        }
    }
}
