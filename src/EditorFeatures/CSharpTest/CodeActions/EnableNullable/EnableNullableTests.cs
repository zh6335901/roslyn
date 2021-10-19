﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.Editor.UnitTests.CodeActions.CSharpCodeRefactoringVerifier<
    Microsoft.CodeAnalysis.CSharp.CodeRefactorings.EnableNullable.EnableNullableCodeRefactoringProvider>;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.CodeActions.EnableNullable
{
    public class EnableNullableTests
    {
        private static readonly Func<Solution, ProjectId, Solution> s_enableNullableInFixedSolution =
            (solution, projectId) =>
            {
                var project = solution.GetRequiredProject(projectId);
                var document = project.Documents.First();

                // Only the input solution contains '#nullable enable'
                if (!document.GetTextSynchronously(CancellationToken.None).ToString().Contains("#nullable enable"))
                {
                    var compilationOptions = (CSharpCompilationOptions)solution.GetRequiredProject(projectId).CompilationOptions!;
                    solution = solution.WithProjectCompilationOptions(projectId, compilationOptions.WithNullableContextOptions(NullableContextOptions.Enable));
                }

                return solution;
            };

        [Fact]
        public async Task EnabledOnNullableEnable()
        {
            var code1 = @"
#nullable enable$$

class Example
{
  string? value;
}
";
            var code2 = @"
class Example2
{
  string value;
}
";
            var code3 = @"
class Example3
{
#nullable enable
  string? value;
#nullable restore
}
";
            var code4 = @"
#nullable disable

class Example4
{
  string value;
}
";

            var fixedCode1 = @"

class Example
{
  string? value;
}
";
            var fixedCode2 = @"
#nullable disable

class Example2
{
  string value;
}
";
            var fixedCode3 = @"
#nullable disable

class Example3
{
#nullable restore
  string? value;
#nullable disable
}
";
            var fixedCode4 = @"
#nullable disable

class Example4
{
  string value;
}
";

            await new VerifyCS.Test
            {
                TestState =
                {
                    Sources =
                    {
                        code1,
                        code2,
                        code3,
                        code4,
                    },
                },
                FixedState =
                {
                    Sources =
                    {
                        fixedCode1,
                        fixedCode2,
                        fixedCode3,
                        fixedCode4,
                    },
                },
                SolutionTransforms = { s_enableNullableInFixedSolution },
            }.RunAsync();
        }

        [Fact]
        public async Task PlacementAfterHeader()
        {
            var code1 = @"
#nullable enable$$

class Example
{
  string? value;
}
";
            var code2 = @"// File header line 1
// File header line 2

class Example2
{
  string value;
}
";
            var code3 = @"#region File Header
// File header line 1
// File header line 2
#endregion

class Example3
{
  string value;
}
";

            var fixedCode1 = @"

class Example
{
  string? value;
}
";
            var fixedCode2 = @"// File header line 1
// File header line 2

#nullable disable

class Example2
{
  string value;
}
";
            var fixedCode3 = @"#region File Header
// File header line 1
// File header line 2
#endregion

#nullable disable

class Example3
{
  string value;
}
";

            await new VerifyCS.Test
            {
                TestState =
                {
                    Sources =
                    {
                        code1,
                        code2,
                        code3,
                    },
                },
                FixedState =
                {
                    Sources =
                    {
                        fixedCode1,
                        fixedCode2,
                        fixedCode3,
                    },
                },
                SolutionTransforms = { s_enableNullableInFixedSolution },
            }.RunAsync();
        }

        [Fact]
        public async Task OmitLeadingRestore()
        {
            var code1 = @"
#nullable enable$$

class Example
{
  string? value;
}
";
            var code2 = @"
#nullable enable

class Example2
{
  string? value;
}
";
            var code3 = @"
#nullable enable warnings

class Example3
{
  string value;
}
";
            var code4 = @"
#nullable enable annotations

class Example4
{
  string? value;
}
";

            var fixedCode1 = @"

class Example
{
  string? value;
}
";
            var fixedCode2 = @"

class Example2
{
  string? value;
}
";
            var fixedCode3 = @"
#nullable disable

#nullable restore warnings

class Example3
{
  string value;
}
";
            var fixedCode4 = @"
#nullable disable

#nullable restore annotations

class Example4
{
  string? value;
}
";

            await new VerifyCS.Test
            {
                TestState =
                {
                    Sources =
                    {
                        code1,
                        code2,
                        code3,
                        code4,
                    },
                },
                FixedState =
                {
                    Sources =
                    {
                        fixedCode1,
                        fixedCode2,
                        fixedCode3,
                        fixedCode4,
                    },
                },
                SolutionTransforms = { s_enableNullableInFixedSolution },
            }.RunAsync();
        }

        [Fact]
        public async Task IgnoreGeneratedCode()
        {
            var code1 = @"
#nullable enable$$

class Example
{
  string? value;
}
";
            var generatedCode1 = @"// <auto-generated/>

#nullable enable

class Example2
{
  string? value;
}
";
            var generatedCode2 = @"// <auto-generated/>

#nullable disable

class Example3
{
  string value;
}
";
            var generatedCode3 = @"// <auto-generated/>

#nullable restore

class Example4
{
  string {|#0:value|};
}
";

            var fixedCode1 = @"

class Example
{
  string? value;
}
";

            await new VerifyCS.Test
            {
                TestState =
                {
                    Sources =
                    {
                        code1,
                        generatedCode1,
                        generatedCode2,
                        generatedCode3,
                    },
                },
                FixedState =
                {
                    Sources =
                    {
                        fixedCode1,
                        generatedCode1,
                        generatedCode2,
                        generatedCode3,
                    },
                    ExpectedDiagnostics =
                    {
                        // /0/Test3.cs(7,10): error CS8618: Non-nullable field 'value' must contain a non-null value when exiting constructor. Consider declaring the field as nullable.
                        DiagnosticResult.CompilerError("CS8618").WithLocation(0),
                    },
                },
                SolutionTransforms = { s_enableNullableInFixedSolution },
            }.RunAsync();
        }

        [Theory]
        [InlineData(NullableContextOptions.Annotations)]
        [InlineData(NullableContextOptions.Warnings)]
        [InlineData(NullableContextOptions.Enable)]
        public async Task DisabledIfSetInProject(NullableContextOptions nullableContextOptions)
        {
            var code = @"
#nullable enable$$
";

            await new VerifyCS.Test
            {
                TestCode = code,
                FixedCode = code,
                SolutionTransforms =
                {
                    (solution, projectId) =>
                    {
                        var compilationOptions = (CSharpCompilationOptions)solution.GetRequiredProject(projectId).CompilationOptions!;
                        return solution.WithProjectCompilationOptions(projectId, compilationOptions.WithNullableContextOptions(nullableContextOptions));
                    },
                },
            }.RunAsync();
        }

        [Fact]
        public async Task DisabledOnNullableDisable()
        {
            var code = @"
#nullable disable$$
";

            await new VerifyCS.Test
            {
                TestCode = code,
                FixedCode = code,
            }.RunAsync();
        }
    }
}
