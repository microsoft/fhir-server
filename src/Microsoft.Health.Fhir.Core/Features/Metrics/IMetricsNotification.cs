// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    public interface IMetricsNotification : INotification
    {
        string FhirOperation { get; }

        string ResourceType { get; }
    }
}
