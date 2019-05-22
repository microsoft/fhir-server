// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    public interface ICodeGenerator
    {
        (MemberDeclarationSyntax, UsingDirectiveSyntax[]) Generate(string typeName);
    }
}
