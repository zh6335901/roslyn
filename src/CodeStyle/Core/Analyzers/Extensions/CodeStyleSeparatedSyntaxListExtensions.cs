﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CodeAnalysis
{
    internal static class CodeStyleSeparatedSyntaxListExtensions
    {
        public static SeparatedSyntaxList<TDerived> CastDown<TDerived>(this SeparatedSyntaxList<SyntaxNode> list)
            where TDerived : SyntaxNode
        {
            return list;
        }
    }
}
