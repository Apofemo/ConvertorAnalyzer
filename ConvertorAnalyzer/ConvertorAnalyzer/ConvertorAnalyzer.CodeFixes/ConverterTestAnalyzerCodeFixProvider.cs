using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertorAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConverterTestAnalyzerCodeFixProvider)), Shared]
    public class ConverterTestAnalyzerCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
             => ImmutableArray.Create(ConverterTestAnalyzerAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var solution = context.Document.Project.Solution;

            var diagnostic = context.Diagnostics.First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedDocument: c => GenerateAsserts(context.Document, solution, diagnostic, root, c),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private async Task<Document> GenerateAsserts(Document document, Solution solution, Diagnostic diagnostic, SyntaxNode root, CancellationToken cancellationToken)
        {
            var statement = root.FindNode(diagnostic.Location.SourceSpan) as TypeArgumentListSyntax;

            var nodeToChange = statement?
                .Parent?
                .Parent?
                .Parent?
                .Parent?
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is BaseTypeDeclarationSyntax)
                as BaseTypeDeclarationSyntax;

            if (nodeToChange == null)
                return null;

            var genericName = ((GenericNameSyntax)statement.Parent)
                .Identifier
                .Text;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null || statement.Arguments.Count != 2)
                return null;

            var fromClassName = string.Empty;
            var fromPropNames = new List<string>();

            var toClassName = string.Empty;
            var toPropNames = new List<string>();

            switch (statement.Arguments[0])
            {
                case IdentifierNameSyntax argument:
                    fromClassName = argument.Identifier.Text;
                    fromPropNames = await GetCsProperties(solution, argument, cancellationToken);
                    break;

                case QualifiedNameSyntax argument:
                    fromClassName = argument.ToFullString();
                    fromPropNames = GetFsProperties(semanticModel, argument.Right);
                    break;
            }

            switch (statement.Arguments[1])
            {
                case IdentifierNameSyntax argument:
                    toClassName = argument.Identifier.Text;
                    toPropNames = await GetCsProperties(solution, argument, cancellationToken);
                    break;

                case QualifiedNameSyntax argument:
                    toClassName = argument.ToFullString();
                    toPropNames = GetFsProperties(semanticModel, argument.Right);
                    break;
            }

            if (string.IsNullOrWhiteSpace(fromClassName) || string.IsNullOrWhiteSpace(toClassName))
                return null;

            var newTree = SyntaxFactory.CompilationUnit()
                .WithMembers(
                    SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                        SyntaxFactory.ClassDeclaration(nodeToChange.Identifier.Text)
                            .WithModifiers(
                                SyntaxFactory.TokenList(
                                    nodeToChange.Modifiers))
                            .WithBaseList(
                                SyntaxFactory.BaseList(
                                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                                        SyntaxFactory.SimpleBaseType(
                                            SyntaxFactory.GenericName(
                                                    SyntaxFactory.Identifier(genericName))
                                                .WithTypeArgumentList(
                                                    SyntaxFactory.TypeArgumentList(
                                                        SyntaxFactory.SeparatedList<TypeSyntax>(
                                                            new SyntaxNodeOrToken[]
                                                            {
                                                                SyntaxFactory.IdentifierName(fromClassName),
                                                                SyntaxFactory.Token(SyntaxKind.CommaToken),
                                                                SyntaxFactory.IdentifierName(toClassName)
                                                            })))))))
                            .WithMembers(
                                SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                                    SyntaxFactory.MethodDeclaration(
                                            SyntaxFactory.PredefinedType(
                                                SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                                            SyntaxFactory.Identifier("TestScenario"))
                                        .WithModifiers(
                                            SyntaxFactory.TokenList(
                                                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                                                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
                                        .WithParameterList(
                                            SyntaxFactory.ParameterList(
                                                SyntaxFactory.SeparatedList<ParameterSyntax>(
                                                    new SyntaxNodeOrToken[]
                                                    {
                                                        SyntaxFactory.Parameter(
                                                                SyntaxFactory.Identifier("expected"))
                                                            .WithType(
                                                                SyntaxFactory.IdentifierName(fromClassName)),
                                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                                        SyntaxFactory.Parameter(
                                                                SyntaxFactory.Identifier("tested"))
                                                            .WithType(
                                                                SyntaxFactory.IdentifierName(toClassName))
                                                    })))
                                        .WithBody(
                                            SyntaxFactory.Block(GetAssertBlockSyntax(fromPropNames, toPropNames)))))));

            var newNode = (await newTree
                    .SyntaxTree
                    .GetRootAsync(cancellationToken))
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is ClassDeclarationSyntax);

            if (newNode == null)
                return null;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

            editor.ReplaceNode(nodeToChange, newNode);

            return editor.GetChangedDocument();
        }

        private static async Task<List<string>> GetCsProperties(Solution solution, TypeSyntax type, CancellationToken cancellationToken)
        {
            var document = solution.GetDocument(type.SyntaxTree);

            if (document == null)
                return new List<string>();

            var correctSemanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var symbolInfo = correctSemanticModel.GetSymbolInfo(type, cancellationToken);

            if (symbolInfo.Symbol == null)
                return new List<string>();

            var declarationSyntaxReference = symbolInfo
                .Symbol
                .DeclaringSyntaxReferences
                .FirstOrDefault();

            if (declarationSyntaxReference == null)
                return new List<string>();

            var declarationSyntax = await declarationSyntaxReference
                .GetSyntaxAsync(cancellationToken)
                as BaseTypeDeclarationSyntax;

            if (declarationSyntax == null)
                return new List<string>();

            var propertyDeclaration = declarationSyntax
                .DescendantNodes()
                .OfType<PropertyDeclarationSyntax>();

            var result = propertyDeclaration
                .Select(property =>
                {
                    if (property.Type.DescendantNodes().Any(node => node is TypeArgumentListSyntax))
                        return property.Identifier.Text + "/*Collection*/";

                    return property.Identifier.Text;
                })
                .ToList();

            var parentType = declarationSyntax
                .BaseList?
                .Types
                .FirstOrDefault()?
                .Type;

            if (parentType != null)
            {
                switch (parentType)
                {
                    case IdentifierNameSyntax _:
                        result.AddRange(await GetCsProperties(solution, parentType, cancellationToken));
                        break;
                    case QualifiedNameSyntax parentSyntax:
                        result.AddRange(await GetCsProperties(solution, parentSyntax.Right, cancellationToken));
                        break;
                }
            }

            return result;
        }

        private static List<string> GetFsProperties(SemanticModel semanticModel, TypeSyntax type)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(type);

            if (symbolInfo.Symbol == null)
                return new List<string>();

            var memberNames = ((INamedTypeSymbol)symbolInfo.Symbol)
                .MemberNames
                .Where(n => !n.Contains("get_")
                            && !n.Contains("@")
                            && n != ".ctor"
                            && n != "ToString"
                            && n != "CompareTo"
                            && n != "GetHashCode"
                            && n != "Equals")
                .ToList();

            return memberNames;
        }

        private static IEnumerable<StatementSyntax> GetAssertBlockSyntax(List<string> fromProperties, List<string> toProperties)
        {
            var result = new List<StatementSyntax>
            {
                GetAssertNotNullSyntax("tested"),
                GetAssertNotNullSyntax("expected"),
                NewLine()
            };

            foreach (var from in fromProperties)
            {
                var to = toProperties.FirstOrDefault(toProp => toProp.Equals(from, StringComparison.OrdinalIgnoreCase))
                         ?? toProperties.FirstOrDefault(toProp => toProp.IndexOf(from, StringComparison.OrdinalIgnoreCase) > -1
                                                             || from.IndexOf(toProp, StringComparison.OrdinalIgnoreCase) > -1);

                if (to == null)
                    to = "/*unexpected mismatch*/";
                else
                    toProperties.Remove(to);

                if (from.Contains("/*Collection*/"))
                {
                    result.Add(NewLine());
                    result.Add(GetAssertNotNullSyntax("expected." + from.Replace("/*Collection*/", string.Empty)));
                    result.Add(GetAssertCountSyntax(from, to));
                }
                else
                    result.Add(GetAssertPropertySyntax(from, to));
            }

            if (toProperties.Any())
            {
                foreach (var to in toProperties)
                {

                    if (to.Contains("/*Collection*/"))
                    {
                        result.Add(NewLine());
                        result.Add(GetAssertNotNullSyntax("tested." + to.Replace("/*Collection*/", string.Empty)));
                        result.Add(GetAssertCountSyntax("/*unexpected mismatch*/", to));
                    }
                    else
                        result.Add(GetAssertPropertySyntax(to, "/*unexpected mismatch*/"));
                }
            }

            return result;
        }

        private static StatementSyntax GetAssertPropertySyntax(string from, string to)
            => SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("Assert"),
                            SyntaxFactory.IdentifierName("That")))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                new SyntaxNodeOrToken[]
                                {
                                    SyntaxFactory.Argument(
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            SyntaxFactory.IdentifierName("tested"),
                                            SyntaxFactory.IdentifierName(to))),
                                    SyntaxFactory.Token(SyntaxKind.CommaToken),
                                    SyntaxFactory.Argument(
                                        SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName("Is"),
                                                    SyntaxFactory.IdentifierName("EqualTo")))
                                            .WithArgumentList(
                                                SyntaxFactory.ArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Argument(
                                                            SyntaxFactory.MemberAccessExpression(
                                                                SyntaxKind.SimpleMemberAccessExpression,
                                                                SyntaxFactory.IdentifierName("expected"),
                                                                SyntaxFactory.IdentifierName(from)))))))

                                }))));

        private static StatementSyntax GetAssertCountSyntax(string from, string to)
            => SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("Assert"),
                            SyntaxFactory.IdentifierName("That")))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                new SyntaxNodeOrToken[]
                                {
                                    SyntaxFactory.Argument(
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.IdentifierName("tested"),
                                                SyntaxFactory.IdentifierName(to.Replace("/*Collection*/", string.Empty))),
                                            SyntaxFactory.IdentifierName("Length"))),
                                    SyntaxFactory.Token(SyntaxKind.CommaToken),
                                    SyntaxFactory.Argument(
                                        SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName("Is"),
                                                    SyntaxFactory.IdentifierName("EqualTo")))
                                            .WithArgumentList(
                                                SyntaxFactory.ArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Argument(
                                                            SyntaxFactory.MemberAccessExpression(
                                                                SyntaxKind.SimpleMemberAccessExpression,
                                                                SyntaxFactory.MemberAccessExpression(
                                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                                    SyntaxFactory.IdentifierName("expected"),
                                                                    SyntaxFactory.IdentifierName(from.Replace("/*Collection*/", string.Empty))),
                                                                SyntaxFactory.IdentifierName("Count")))))))

                                }))));

        private static StatementSyntax GetAssertNotNullSyntax(string name)
            => SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("Assert"),
                                SyntaxFactory.IdentifierName("That")))
                        .WithArgumentList(
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                    new SyntaxNodeOrToken[]
                                    {
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.IdentifierName(name)),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName("Is"),
                                                    SyntaxFactory.IdentifierName("Not")),
                                                SyntaxFactory.IdentifierName("Null")))
                                    }))));

        private static StatementSyntax NewLine()
            => SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.IdentifierName("\n\t\t"))
                .WithSemicolonToken(
                    SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken));
    }
}
