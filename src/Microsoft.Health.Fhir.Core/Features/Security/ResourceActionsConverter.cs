// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Globalization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class ResourceActionsConverter : EnumConverter
    {
        public ResourceActionsConverter()
            : base(typeof(ResourceActions))
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value as string == "*")
            {
                return ResourceActions.All;
            }

            return base.ConvertFrom(context, culture, value);
        }
    }
}
