// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Globalization;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Binds the legacy scalar <c>CoreFeatures:FhirSdkProvider</c> value (for example <c>"Ignixa"</c>)
    /// to <see cref="FhirSdkProviderConfiguration.Default"/>, so existing deployments keep their selection.
    /// </summary>
    internal sealed class FhirSdkProviderConfigurationConverter : TypeConverter
    {
        /// <inheritdoc />
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        /// <inheritdoc />
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is not string text)
            {
                return base.ConvertFrom(context, culture, value);
            }

            if (!Enum.TryParse(text.Trim(), ignoreCase: true, out FhirSdkProvider provider)
                || !Enum.IsDefined(provider)
                || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new FormatException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "'{0}' is not a valid {1}. Expected one of: {2}.",
                        text,
                        nameof(FhirSdkProvider),
                        string.Join(", ", Enum.GetNames<FhirSdkProvider>())));
            }

            return new FhirSdkProviderConfiguration { Default = provider };
        }
    }
}
