// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Accepts a reference only when the active SQL schema stores and compares every component whole.
    /// Reference ids and base URIs live in varchar columns, so a value must fit the column length and the column code page.
    /// </summary>
    internal sealed class SqlReferenceStorageValidator : IReferenceStorageValidator
    {
        // Schema versions before V118 declared root and reference resource ids as varchar(64) in tables, table types, and procedure parameters.
        private const int PreV118ResourceIdCapacity = 64;

        private static readonly int ResourceIdCapacity = (int)Math.Min(
            VLatest.Resource.ResourceId.Metadata.MaxLength,
            Math.Min(
                VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.MaxLength,
                VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.MaxLength));

        private static readonly int BaseUriCapacity = (int)Math.Min(
            VLatest.ReferenceSearchParam.BaseUri.Metadata.MaxLength,
            VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.MaxLength);

        // Latin1_General_100_CS_AS varchar columns use Windows code page 1252. SQL Server replaces other characters, so distinct values could collide.
        private static readonly Encoding ColumnEncoding = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private readonly SchemaInformation _schemaInformation;

        public SqlReferenceStorageValidator(SchemaInformation schemaInformation)
        {
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
        }

        public void EnsureRepresentable(ReferenceSearchValue value)
        {
            EnsureArg.IsNotNull(value, nameof(value));

            if (value.BaseUri != null)
            {
                EnsureFieldRepresentable(FieldName.ReferenceBaseUri, value.BaseUri.ToString());
            }

            EnsureFieldRepresentable(FieldName.ReferenceResourceId, value.ResourceId);
        }

        /// <summary>
        /// Gets the longest resource id that every root and reference resource-id column of the active schema stores whole.
        /// </summary>
        /// <param name="schemaInformation">The active schema, or <c>null</c> before it is known.</param>
        /// <returns>The capacity in characters. An unknown schema gets the smaller pre-V118 capacity.</returns>
        internal static int GetResourceIdCapacity(SchemaInformation schemaInformation)
            => schemaInformation?.Current >= SchemaVersionConstants.ReferenceResourceIdLength128 ? ResourceIdCapacity : PreV118ResourceIdCapacity;

        /// <summary>
        /// Ensures one serialized reference component is stored and compared without shortening or character substitution.
        /// </summary>
        /// <param name="component">Either <see cref="FieldName.ReferenceResourceId"/> or <see cref="FieldName.ReferenceBaseUri"/>.</param>
        /// <param name="exactValue">The exact value that would be written or bound as a parameter.</param>
        /// <exception cref="ReferenceStorageCapacityException">Thrown when the value cannot be represented whole.</exception>
        internal void EnsureFieldRepresentable(FieldName component, string exactValue)
        {
            EnsureArg.IsNotNull(exactValue, nameof(exactValue));

            int capacity = component switch
            {
                FieldName.ReferenceResourceId => GetResourceIdCapacity(_schemaInformation),
                FieldName.ReferenceBaseUri => BaseUriCapacity,
                _ => throw new ArgumentOutOfRangeException(nameof(component)),
            };

            if (exactValue.Length > capacity || !IsInColumnCodePage(exactValue))
            {
                throw new ReferenceStorageCapacityException(component, capacity);
            }
        }

        private static bool IsInColumnCodePage(string value)
        {
            if (Ascii.IsValid(value))
            {
                return true;
            }

            try
            {
                ColumnEncoding.GetByteCount(value);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }
    }
}
