// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceSerializer : IResourceSerializer
    {
        private readonly IReadOnlyDictionary<FhirResourceFormat, Func<Resource, string>> _serializers;

        public ResourceSerializer(IReadOnlyDictionary<FhirResourceFormat, Func<Resource, string>> serializers)
        {
            EnsureArg.IsNotNull(serializers, nameof(serializers));
            _serializers = serializers;
        }

        public ResourceSerializer(params (FhirResourceFormat Format, Func<Resource, string> Func)[] serializers)
        {
            EnsureArg.IsNotNull(serializers, nameof(serializers));
            _serializers = serializers.ToDictionary(x => x.Format, x => x.Func);
        }

        public string Serialize(Resource resource, FhirResourceFormat format)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            if (!_serializers.TryGetValue(format, out var serializer))
            {
                throw new NotSupportedException();
            }

            return serializer(resource);
        }
    }
}
