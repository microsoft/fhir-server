// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    public static class MemberSorting
    {
        private const string SortKey = "SortKey";

        public static readonly Comparer<MemberDeclarationSyntax> Comparer = Comparer<MemberDeclarationSyntax>.Create(CompareMembers);

        private static int CompareMembers(MemberDeclarationSyntax a, MemberDeclarationSyntax b)
        {
            string[] GetSortKey(MemberDeclarationSyntax member)
            {
                return member.GetAnnotations(SortKey).SingleOrDefault()?.Data.Split(':') ?? throw new InvalidOperationException("Members are required to have a sort key");
            }

            if (a == b)
            {
                return 0;
            }

            if (a == null)
            {
                return -1;
            }

            if (b == null)
            {
                return 1;
            }

            return GetSortKey(a).Zip(GetSortKey(b), (ta, tb) => (tokenA: ta, tokenB: tb)).Aggregate(0, (acc, curr) => acc != 0 ? acc : string.CompareOrdinal(curr.tokenA, curr.tokenB));
        }

        public static TMember AddSortingKey<TMember>(this TMember member, SqlVisitor visitor, string name)
            where TMember : MemberDeclarationSyntax
        {
            return member.WithAdditionalAnnotations(new SyntaxAnnotation(SortKey, $"{(member is FieldDeclarationSyntax ? 0 : 1)}:{visitor.ArtifactSortOder}:{name}"));
        }
    }
}
