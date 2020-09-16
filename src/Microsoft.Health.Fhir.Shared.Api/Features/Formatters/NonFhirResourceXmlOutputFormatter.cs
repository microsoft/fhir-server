// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public class NonFhirResourceXmlOutputFormatter : XmlSerializerOutputFormatter
    {
        protected override bool CanWriteType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            if (typeof(Resource).IsAssignableFrom(type))
            {
                return false;
            }

            return base.CanWriteType(type);
        }
    }
}
