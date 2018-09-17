// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Superpower.Display;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser
{
    /// <summary>
    /// The token types of a template expression.
    /// </summary>
    internal enum TemplateExpressionToken
    {
        /// <summary>
        /// Represents an unrecognized token
        /// </summary>
        None,

        /// <summary>
        /// An opening parenthesis
        /// </summary>
        [Token(Example = "(")]
        LParen,

        /// <summary>
        /// A closing parenthesis
        /// </summary>
        [Token(Example = ")")]
        RParen,

        /// <summary>
        /// A comma
        /// </summary>
        [Token(Example = ",")]
        Comma,

        /// <summary>
        /// A single quote
        /// </summary>
        [Token(Example = "'")]
        Quote,

        /// <summary>
        /// A minus token
        /// </summary>
        [Token(Example = "-")]
        Minus,

        /// <summary>
        /// An opening brace
        /// </summary>
        [Token(Example = "{")]
        LBrace,

        /// <summary>
        /// A closing brace
        /// </summary>
        [Token(Example = "}")]
        RBrace,

        /// <summary>
        /// A string segment that is a string literal (as opposed to an expression
        /// </summary>
        StringLiteralSegment,

        /// <summary>
        /// A number
        /// </summary>
        Number,

        /// <summary>
        /// An identifier
        /// </summary>
        Identifier,
    }
}
