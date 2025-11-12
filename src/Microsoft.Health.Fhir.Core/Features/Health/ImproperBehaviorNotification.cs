// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Medino;

namespace Microsoft.Health.Fhir.Core.Features.Health
{
    public class ImproperBehaviorNotification : INotification
    {
        public ImproperBehaviorNotification(string message)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Message = message;
        }

        public string Message { get; }
    }
}
