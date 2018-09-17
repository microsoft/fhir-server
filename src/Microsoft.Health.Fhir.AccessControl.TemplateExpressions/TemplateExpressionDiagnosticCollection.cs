// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions
{
    /// <summary>
    /// A collection of <see cref="TemplateExpressionDiagnostic"/>s.
    /// </summary>
    public class TemplateExpressionDiagnosticCollection : IReadOnlyCollection<TemplateExpressionDiagnostic>
    {
        private readonly List<TemplateExpressionDiagnostic> _list = new List<TemplateExpressionDiagnostic>();

        public int Count => _list.Count;

        public void Add(TextSpan textSpan, string messageFormat, params object[] args)
        {
            Add(new TemplateExpressionDiagnostic(textSpan, messageFormat, args));
        }

        public void Add(TemplateExpressionDiagnostic diagnostic)
        {
            _list.Add(diagnostic);
        }

        public IEnumerator<TemplateExpressionDiagnostic> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_list).GetEnumerator();
        }
    }
}
