// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceIdProvider
    {
        private Func<string> _resourceId;

        public ResourceIdProvider()
        {
            _resourceId = () => Guid.NewGuid().ToString();
        }

        public Func<string> Create
        {
            get
            {
                return _resourceId;
            }

            set
            {
                _resourceId = value;
            }
        }
    }
}
