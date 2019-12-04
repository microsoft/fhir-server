// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceIdProvider
    {
        private readonly AsyncLocal<Func<string>> _resourceId = new AsyncLocal<Func<string>>();

        public ResourceIdProvider()
        {
            _resourceId.Value = () => Guid.NewGuid().ToString();
        }

        public Func<string> Create
        {
            get
            {
                return _resourceId.Value;
            }

            set
            {
                _resourceId.Value = value;
            }
        }
    }
}
