// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Schema
{
    public class GetCompatibilityVersionRequest : IRequest<GetCompatibilityVersionResponse>
    {
        public GetCompatibilityVersionRequest(int minVersion, int maxVersion)
        {
            MinVersion = minVersion;
            MaxVersion = maxVersion;
        }

        public int MinVersion { get; }

        public int MaxVersion { get; }
    }
}
