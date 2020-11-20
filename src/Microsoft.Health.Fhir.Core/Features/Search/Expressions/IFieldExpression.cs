// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public interface IFieldExpression
    {
        /// <summary>
        /// Gets the field name.
        /// </summary>
        FieldName FieldName { get; }

        /// <summary>
        /// Gets the optional component index.
        /// </summary>
        /// <remarks>
        /// Should be present only in case of composite search parameters.
        /// For example `code-value-string=http://snomed.info/sct|162806009$blue`
        /// would be broke into two expressions 'code = http://snomed.info/sct' and
        /// 'value-string =162806009$blue'
        /// <see cref="ComponentIndex"/> for first expression would be 1 and 2 for second.
        /// </remarks>
        int? ComponentIndex { get; }
    }
}
