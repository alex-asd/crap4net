using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crap4Net;

/// <summary>
/// Finds every member with a body (methods, constructors, finalizers, operators, accessors,
/// expression-bodied properties, and a file's top-level statements) and measures its complexity.
/// Generated code is skipped: whole files marked auto-generated, members or types marked
/// [ExcludeFromCodeCoverage]/[GeneratedCode], and EF Core migration scaffolding.
/// </summary>
internal static class CSharpMethodParser
{
    public const string TopLevelName = "<top-level>";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    private static readonly string[] GeneratedFileSuffixes = [".g.cs", ".g.i.cs", ".designer.cs", ".generated.cs"];

    private static readonly HashSet<string> ExcludingAttributes = ["ExcludeFromCodeCoverage", "GeneratedCode"];

    // EF Core scaffolds these from the model; their size says nothing about hand-written risk.
    private static readonly HashSet<string> ScaffoldedBaseTypes = ["Migration", "ModelSnapshot"];

    public static IReadOnlyList<MethodDescriptor> ParseFile(string path) =>
        Parse(File.ReadAllText(path), Path.GetFullPath(path));

    public static IReadOnlyList<MethodDescriptor> Parse(string source, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, filePath);
        var root = tree.GetCompilationUnitRoot();
        if (IsGeneratedFile(filePath, root))
            return [];

        var members = root.DescendantNodes()
            .Where(node => HasBody(node) && !IsExcluded(node))
            .Select(node => Describe(tree, node));
        return TopLevelStatements(tree).Concat(members).ToList();
    }

    private static IEnumerable<MethodDescriptor> TopLevelStatements(SyntaxTree tree)
    {
        var statements = tree.GetCompilationUnitRoot().Members.OfType<GlobalStatementSyntax>().ToList();
        if (statements.Count == 0)
            yield break;
        yield return new MethodDescriptor(
            TopLevelName, "Program", "", tree.FilePath,
            LineOf(tree, statements[0].Span.Start),
            LineOf(tree, statements[0].Span.Start),
            LineOf(tree, statements[^1].Span.End),
            ComplexityCounter.Count(statements));
    }

    /// <summary>Whether the node is a member this tool scores: one with code in it.</summary>
    private static bool HasBody(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax m => m.Body is not null || m.ExpressionBody is not null,
        AccessorDeclarationSyntax a => a.Body is not null || a.ExpressionBody is not null,
        PropertyDeclarationSyntax p => p.ExpressionBody is not null,
        IndexerDeclarationSyntax i => i.ExpressionBody is not null,
        _ => false,
    };

    private static MethodDescriptor Describe(SyntaxTree tree, SyntaxNode member)
    {
        var (name, anchor) = Identify(member);
        return new MethodDescriptor(
            name,
            ContainingTypeName(member),
            NamespaceOf(member),
            tree.FilePath,
            LineOf(tree, anchor.SpanStart),
            LineOf(tree, member.Span.Start),
            LineOf(tree, member.Span.End),
            ComplexityCounter.Count(member));
    }

    /// <summary>The report name of a member <see cref="HasBody"/> accepted, and the token its location points at.</summary>
    private static (string Name, SyntaxToken Anchor) Identify(SyntaxNode member) => member switch
    {
        BaseMethodDeclarationSyntax method => IdentifyMethod(method),
        AccessorDeclarationSyntax a => ($"{AccessorOwnerName(a)}.{a.Keyword.Text}", a.Keyword),
        PropertyDeclarationSyntax p => ($"{p.Identifier.Text}.get", p.Identifier),
        IndexerDeclarationSyntax i => ("this[].get", i.ThisKeyword),
        _ => throw new UnreachableException(member.Kind().ToString()),
    };

    private static (string Name, SyntaxToken Anchor) IdentifyMethod(BaseMethodDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => (m.Identifier.Text, m.Identifier),
        ConstructorDeclarationSyntax c => (c.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor", c.Identifier),
        DestructorDeclarationSyntax d => ("Finalize", d.Identifier),
        OperatorDeclarationSyntax o => ($"operator {o.OperatorToken.Text}", o.OperatorToken),
        ConversionOperatorDeclarationSyntax c => ($"{c.ImplicitOrExplicitKeyword.Text} operator {c.Type}", c.OperatorKeyword),
        _ => throw new UnreachableException(member.Kind().ToString()),
    };

    private static string AccessorOwnerName(AccessorDeclarationSyntax accessor) => accessor.Parent?.Parent switch
    {
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        _ => "?",
    };

    private static bool IsGeneratedFile(string filePath, CompilationUnitSyntax root)
    {
        if (GeneratedFileSuffixes.Any(s => filePath.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            return true;
        return root.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .Select(t => t.ToString())
            .Any(c => c.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
                      || c.Contains("<autogenerated", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcluded(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var attributes = ancestor switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                AccessorDeclarationSyntax accessor => accessor.AttributeLists,
                _ => default,
            };
            if (attributes.SelectMany(l => l.Attributes).Any(a => ExcludingAttributes.Contains(AttributeName(a))))
                return true;
            if (ancestor is TypeDeclarationSyntax { BaseList: { } baseList }
                && baseList.Types.Any(t => ScaffoldedBaseTypes.Contains(SimpleName(t.Type))))
                return true;
        }
        return false;
    }

    private static string AttributeName(AttributeSyntax attribute)
    {
        var name = SimpleName(attribute.Name);
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    private static string SimpleName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax q => SimpleName(q.Right),
        AliasQualifiedNameSyntax a => SimpleName(a.Name),
        GenericNameSyntax g => g.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        _ => type.ToString(),
    };

    // C# 14 extension blocks are unnamed type declarations; their members belong to the enclosing class.
    private static string ContainingTypeName(SyntaxNode node) => string.Join('.', node.Ancestors()
        .OfType<BaseTypeDeclarationSyntax>()
        .Where(t => t.Identifier.Text.Length > 0)
        .Reverse()
        .Select(TypeName));

    private static string TypeName(BaseTypeDeclarationSyntax type) =>
        type is TypeDeclarationSyntax { TypeParameterList: { } parameters }
            ? $"{type.Identifier.Text}<{string.Join(",", parameters.Parameters.Select(p => p.Identifier.Text))}>"
            : type.Identifier.Text;

    private static string NamespaceOf(SyntaxNode node) => string.Join('.',
        node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));

    private static int LineOf(SyntaxTree tree, int position) =>
        tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0)).StartLinePosition.Line + 1;
}
