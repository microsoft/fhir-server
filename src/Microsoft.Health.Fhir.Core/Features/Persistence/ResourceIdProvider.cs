// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceIdProvider
    {
        private int _sequence = RandomNumberGenerator.GetInt32(0, 8999);
        private Func<string> _resourceId;

        public ResourceIdProvider()
        {
            _resourceId = () => Clock.UtcNow.UtcDateTime.ToSequentialGuid(_sequence++).ToString();
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
