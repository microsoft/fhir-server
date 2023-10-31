// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class ResourceWrapperExtention
    {
        public static ResourceDateKey ToResourceDateKey(this ResourceWrapper wrapper, Func<string, short> getResourceTypeId, bool ignoreVersion = false)
        {
            return new ResourceDateKey(getResourceTypeId(wrapper.ResourceTypeName), wrapper.ResourceId, ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(wrapper.LastModified.DateTime), ignoreVersion ? null : wrapper.Version);
        }
    }
}
