using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PhongNguyen.CodeAnalysis.IQueryableAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class IQuerableAnalyzer : DiagnosticAnalyzer
    {
        public const string UseContainsMethodInExpressionRule = "PNIQ001";
        public const string EvaluateIQueryableInsideLoopRule = "PNIQ002";

        private static Dictionary<string, DiagnosticDescriptor> _rules;
        public static Dictionary<string, DiagnosticDescriptor> Rules
        {
            get
            {
                if (_rules == null)
                {
                    _rules = new Dictionary<string, DiagnosticDescriptor>
                {
                    {UseContainsMethodInExpressionRule, new DiagnosticDescriptor(UseContainsMethodInExpressionRule, "Expression has 'Contains' method", "Using '{0}' might generate Adhoc queries.", "Optimazation", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Using 'Contains' cause generating Adhoc queries.") },
                    {EvaluateIQueryableInsideLoopRule, new DiagnosticDescriptor(EvaluateIQueryableInsideLoopRule, "Evaluate IQueryable Inside a Loop", "Using {0} inside a loop might generate multiple queries to database.", "Optimazation", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Evaluate IQueryable Inside a Loop might generate multiple queries to database.") },
                };
                }
                return _rules;
            }
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rules.Values.ToArray()); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeLinqExpression, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression/*, SyntaxKind.QueryExpression*/);
            context.RegisterSyntaxNodeAction(AnalizeLoop, SyntaxKind.WhileStatement, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement);
        }

        private void AnalyzeLinqExpression(SyntaxNodeAnalysisContext context)
        {
            string receiverType = string.Empty;
            string returnType = string.Empty;

            var lambda = context.Node as LambdaExpressionSyntax;

            if (!lambda.Body.ToString().Contains(".Contains("))
            {
                return;
            }

            SyntaxNode foundNode = HasContainsMethodInLamdaExpression(context, lambda);

            if (foundNode == null)
            {
                return;
            }

            if (lambda.Parent is ArgumentSyntax arg)
            {
                var parent = arg.Parent;
                while (parent != null && !(parent is ArgumentListSyntax))
                {
                    parent = parent.Parent;
                }

                if (parent != null)
                {
                    var argList = parent;
                    if (argList?.Parent is InvocationExpressionSyntax invoc)
                    {
                        var si = context.SemanticModel.GetSymbolInfo(invoc.Expression).Symbol as IMethodSymbol;
                        receiverType = si?.ReceiverType?.Name;
                        returnType = si?.ReturnType?.Name;
                    }

                }
            }

            if (receiverType == "IQueryable" || returnType == "IQueryable" || receiverType == "IOrderedQueryable" || returnType == "IOrderedQueryable")
            {
                var diagnostic = Diagnostic.Create(Rules[UseContainsMethodInExpressionRule], foundNode.GetLocation(), foundNode.ToString());

                context.ReportDiagnostic(diagnostic);
            }
        }

        private SyntaxNode HasContainsMethodInLamdaExpression(SyntaxNodeAnalysisContext context, SyntaxNode node)
        {
            SyntaxNode rs = null;

            foreach (var child in node.ChildNodes())
            {
                if (child is IdentifierNameSyntax)
                {
                    var identifier = child as IdentifierNameSyntax;
                    var si = context.SemanticModel.GetSymbolInfo(identifier).Symbol as IMethodSymbol;
                    var type = si?.ReceiverType?.Name;

                    if (identifier.Identifier.ValueText == "Contains" && !string.IsNullOrEmpty(type) && type != "String")
                    {
                        return child;
                    }
                }
                var checkChild = HasContainsMethodInLamdaExpression(context, child);
                if (checkChild != null)
                {
                    return checkChild;
                }
            }
            return rs;
        }

        private void AnalizeLoop(SyntaxNodeAnalysisContext context)
        {
            var node = context.Node;
            TraverseChilds(context, node);
        }

        private void TraverseChilds(SyntaxNodeAnalysisContext context, SyntaxNode node)
        {
            if (node.IsKind(SyntaxKind.ForEachStatement))
            {
                TraverseChilds(context, node.ChildNodes().Last());
                return;
            }

            foreach (var child in node.ChildNodes())
            {
                if (child is InvocationExpressionSyntax)
                {
                    var inv = child as InvocationExpressionSyntax;
                    var si = context.SemanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                    var type = si?.ReceiverType?.Name;
                    var returnType = si?.ReturnType?.Name;

                    if ((type == "IQueryable" || type == "IOrderedQueryable") && returnType != "IQueryable" && returnType != "IOrderedQueryable")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rules[EvaluateIQueryableInsideLoopRule], child.GetLocation(), child.ToString()));
                        return;
                    }
                }

                TraverseChilds(context, child);
            }
        }
    }
}
