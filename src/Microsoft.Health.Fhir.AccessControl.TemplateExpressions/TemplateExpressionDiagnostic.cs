// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using EnsureThat;
using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions
{
    /// <summary>
    /// Represents a syntactic or semantic error in a template expression.
    /// </summary>
    public struct TemplateExpressionDiagnostic : IEquatable<TemplateExpressionDiagnostic>
    {
        public TemplateExpressionDiagnostic(TextSpan textSpan, string messageFormat, params object[] args)
        {
            EnsureArg.IsNotNullOrWhiteSpace(messageFormat, nameof(messageFormat));
            TextSpan = textSpan;
            Message = args?.Length > 0 ? string.Format(CultureInfo.CurrentCulture, messageFormat, args) : messageFormat;
        }

        /// <summary>
        /// The span of text where the error occurs.
        /// </summary>
        public TextSpan TextSpan { get; }

        /// <summary>
        /// THe error message.
        /// </summary>
        public string Message { get; }

        public static bool operator ==(TemplateExpressionDiagnostic left, TemplateExpressionDiagnostic right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TemplateExpressionDiagnostic left, TemplateExpressionDiagnostic right)
        {
            return !(left == right);
        }

        public bool Equals(TemplateExpressionDiagnostic other)
        {
            return TextSpan.Equals(other.TextSpan) && string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TemplateExpressionDiagnostic diagnostic && Equals(diagnostic);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (TextSpan.GetHashCode() * 397) ^ (Message != null ? Message.GetHashCode(StringComparison.Ordinal) : 0);
            }
        }

        public override string ToString()
        {
            Position endPosition = TextSpan.Skip(TextSpan.Length).Position;
            return $"({TextSpan.Position.Line},{TextSpan.Position.Column},{endPosition.Line},{endPosition.Column}): {Message}";
        }
    }
}
