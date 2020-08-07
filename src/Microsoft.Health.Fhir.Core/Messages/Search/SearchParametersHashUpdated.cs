// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchParametersHashUpdated : INotification
    {
        public SearchParametersHashUpdated(string hashValue)
        {
            EnsureArg.IsNotNull(hashValue, nameof(hashValue));

            HashValue = hashValue;
        }

        public string HashValue { get; }
    }
}
