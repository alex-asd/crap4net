using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crap4Net;

/// <summary>
/// McCabe cyclomatic complexity: 1 plus one per decision point. Lambdas and local
/// functions count toward the member that contains them.
/// </summary>
internal sealed class ComplexityCounter : CSharpSyntaxWalker
{
    private int _complexity = 1;

    private ComplexityCounter()
    {
    }

    public static int Count(SyntaxNode node) => Count([node]);

    public static int Count(IEnumerable<SyntaxNode> nodes)
    {
        var counter = new ComplexityCounter();
        foreach (var node in nodes)
            counter.Visit(node);
        return counter._complexity;
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        _complexity++;
        base.VisitIfStatement(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        _complexity++;
        base.VisitConditionalExpression(node);
    }

    // a?.b is a == null ? null : a.b
    public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        _complexity++;
        base.VisitConditionalAccessExpression(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        _complexity++;
        base.VisitForStatement(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        _complexity++;
        base.VisitForEachStatement(node);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        _complexity++;
        base.VisitForEachVariableStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        _complexity++;
        base.VisitWhileStatement(node);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        _complexity++;
        base.VisitDoStatement(node);
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        _complexity++;
        base.VisitCatchClause(node);
    }

    public override void VisitCatchFilterClause(CatchFilterClauseSyntax node)
    {
        _complexity++;
        base.VisitCatchFilterClause(node);
    }

    // `default:` is the fall-through path, like `else`, so it adds nothing.
    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        _complexity++;
        base.VisitCaseSwitchLabel(node);
    }

    public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
    {
        _complexity++;
        base.VisitCasePatternSwitchLabel(node);
    }

    // `_ =>` is the switch expression's default arm.
    public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        if (node.Pattern is not DiscardPatternSyntax)
            _complexity++;
        base.VisitSwitchExpressionArm(node);
    }

    public override void VisitWhenClause(WhenClauseSyntax node)
    {
        _complexity++;
        base.VisitWhenClause(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression)
            _complexity++;
        base.VisitBinaryExpression(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            _complexity++;
        base.VisitAssignmentExpression(node);
    }

    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        if (node.Kind() is SyntaxKind.AndPattern or SyntaxKind.OrPattern)
            _complexity++;
        base.VisitBinaryPattern(node);
    }
}
