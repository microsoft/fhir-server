// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    [AttributeUsage(AttributeTargets.Property)]
    internal class SelectSingleAttribute : Attribute
    {
        public SelectSingleAttribute(string defaultValue)
        {
            EnsureArg.IsNotNull(defaultValue, nameof(defaultValue));

            DefaultValue = defaultValue;
        }

        public string DefaultValue { get; set; }
    }
}
