// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.ConvertData
{
    public class ConvertDataResponse
    {
        public ConvertDataResponse(string resource)
        {
            EnsureArg.IsNotNull(resource);

            Resource = resource;
        }

        public string Resource { get; }
    }
}
