﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class ResourceWrapperExtention
    {
        public static ResourceDateLocationKey ToResourceDateLocationKey(this ResourceWrapper wrapper, Func<string, short> getResourceTypeId, bool ignoreVersion = false)
        {
            return new ResourceDateLocationKey(getResourceTypeId(wrapper.ResourceTypeName), wrapper.ResourceId, wrapper.LastModified.ToSurrogateId(), ignoreVersion ? null : wrapper.Version, wrapper.RawResourceLocator.RawResourceStorageIdentifier, wrapper.RawResourceLocator.RawResourceOffset, wrapper.RawResourceLocator.RawResourceLength);
        }
    }
}
