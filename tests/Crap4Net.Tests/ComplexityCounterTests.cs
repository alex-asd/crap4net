using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crap4Net.Tests;

public class ComplexityCounterTests
{
    private static int Complexity(string body)
    {
        var tree = CSharpSyntaxTree.ParseText($"class C {{ object M() {{ {body} }} }}");
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return ComplexityCounter.Count(method);
    }

    [Theory]
    [InlineData("return null;", 1)]
    [InlineData("if (a) x();", 2)]
    [InlineData("if (a) x(); else if (b) y(); else z();", 3)]
    [InlineData("return a ? 1 : 2;", 2)]
    [InlineData("return a?.B;", 2)]
    [InlineData("return a ?? b;", 2)]
    [InlineData("a ??= b; return a;", 2)]
    [InlineData("if (a && b || c) x();", 4)]
    [InlineData("if (x is not null) y();", 2)]
    public void Counts_branches_and_short_circuits(string body, int expected) =>
        Assert.Equal(expected, Complexity(body));

    [Theory]
    [InlineData("for (;;) { }", 2)]
    [InlineData("foreach (var x in xs) { }", 2)]
    [InlineData("foreach (var (k, v) in d) { }", 2)]
    [InlineData("while (a) { }", 2)]
    [InlineData("do { } while (a);", 2)]
    public void Counts_loops(string body, int expected) =>
        Assert.Equal(expected, Complexity(body));

    [Theory]
    [InlineData("try { } catch (A) { } catch (B) { } finally { }", 3)]
    [InlineData("try { } catch (Exception e) when (e is A) { }", 3)]
    public void Counts_catch_clauses_and_filters(string body, int expected) =>
        Assert.Equal(expected, Complexity(body));

    [Theory]
    [InlineData("switch (x) { case 1: case 2: break; case 3: break; default: break; }", 4)]
    [InlineData("switch (o) { case int i when i > 0: break; case string: break; }", 4)]
    [InlineData("return x switch { 1 => a, 2 => b, _ => c };", 3)]
    [InlineData("return x switch { 1 => a, 2 => b };", 3)]
    [InlineData("return x switch { int i when i > 0 => a, _ => b };", 3)]
    [InlineData("return x switch { > 0 and < 10 => a, 0 or -1 => b, _ => c };", 5)]
    [InlineData("return o is int or long;", 2)]
    public void Counts_cases_arms_and_pattern_combinators_but_not_defaults(string body, int expected) =>
        Assert.Equal(expected, Complexity(body));

    [Theory]
    [InlineData("Func<int, int> f = n => n > 0 ? n : -n; return f;", 2)]
    [InlineData("int Local(int n) { if (n > 0) return n; return 0; } return Local(1);", 2)]
    [InlineData("return xs.Where(x => x != null && x.Ok).Select(x => x ?? y);", 3)]
    public void Folds_lambdas_and_local_functions_into_the_member(string body, int expected) =>
        Assert.Equal(expected, Complexity(body));

    [Fact]
    public void Sums_across_several_nodes()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText("if (a) x(); while (b) y();", cancellationToken: cancellation);
        var statements = tree.GetCompilationUnitRoot(cancellation).Members.OfType<GlobalStatementSyntax>();
        Assert.Equal(3, ComplexityCounter.Count(statements));
    }
}
