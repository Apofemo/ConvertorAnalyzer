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
using System.Threading.Tasks;

namespace ConvertorAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConvertorAnalyzerCodeFixProvider)), Shared]
    public class ConvertorAnalyzerCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(ConvertorAnalyzerAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedDocument: c => GenerateAsserts(context.Document, diagnostic, root),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private async Task<Document> GenerateAsserts(Document document, Diagnostic diagnostic, SyntaxNode root)
        {
            var statement = root.FindNode(diagnostic.Location.SourceSpan) as TypeArgumentListSyntax;

            if (statement == null)
                return null;

            var nodeToChange = statement
                .Parent?
                .Parent?
                .Parent?
                .Parent?
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is ClassDeclarationSyntax || node is RecordDeclarationSyntax)
                as ClassDeclarationSyntax;

            if (nodeToChange == null)
                return null;

            var genericName = ((GenericNameSyntax)statement.Parent)
                .Identifier
                .Text;

            var semanticModel = await document.GetSemanticModelAsync();

            if (semanticModel == null || statement.Arguments.Count != 2)
                return null;

            var fromClassName = string.Empty;
            var fromPropNames = new List<string>();

            var toClassName = string.Empty;
            var toPropNames = new List<string>();

            switch (statement.Arguments[0])
            {
                case IdentifierNameSyntax _:
                    fromClassName = ((IdentifierNameSyntax)statement.Arguments[0]).Identifier.Text;
                    fromPropNames = await GetProps(semanticModel, statement.Arguments[0]);
                    break;
                case QualifiedNameSyntax _:
                    fromClassName = statement.Arguments[0].ToFullString();
                    fromPropNames = GetFsProps(semanticModel, ((QualifiedNameSyntax)statement.Arguments[0]).Right);
                    break;
            }

            switch (statement.Arguments[1])
            {
                case IdentifierNameSyntax _:
                    toClassName = ((IdentifierNameSyntax)statement.Arguments[1]).Identifier.Text;
                    toPropNames = await GetProps(semanticModel, statement.Arguments[1]);
                    break;
                case QualifiedNameSyntax _:
                    toClassName = statement.Arguments[1].ToFullString();
                    toPropNames = GetFsProps(semanticModel, statement.Arguments[1]);
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
                                            SyntaxFactory.Identifier(ConvertorAnalyzerAnalyzer.MethodName))
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
                                            SyntaxFactory.Block(GetAssertBlockSyntax(fromPropNames, toPropNames)))))))
                .NormalizeWhitespace();

            var newNode = (await newTree
                    .SyntaxTree
                    .GetRootAsync())
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is ClassDeclarationSyntax);

            if (newNode == null)
                return null;

            var editor = await DocumentEditor.CreateAsync(document);

            editor.ReplaceNode(nodeToChange, newNode);

            return editor.GetChangedDocument();
        }

        private static async Task<List<string>> GetProps(SemanticModel semanticModel, TypeSyntax type)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(type);

            if (symbolInfo.Symbol == null)
                return default;

            var classDeclarationSyntax = await symbolInfo
                .Symbol
                .DeclaringSyntaxReferences
                .FirstOrDefault()
                .GetSyntaxAsync()
                as ClassDeclarationSyntax;

            if (classDeclarationSyntax == null)
            {
                var recordDeclarationSyntax = await symbolInfo
                    .Symbol
                    .DeclaringSyntaxReferences
                    .FirstOrDefault()
                    .GetSyntaxAsync()
                    as RecordDeclarationSyntax;

                if (recordDeclarationSyntax == null)
                    return default;

                var recordPropertyDeclaration = recordDeclarationSyntax
                    .DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>();

                var recordResult = recordPropertyDeclaration
                    .Select(property => property.Identifier.Text)
                    .ToList();

                return recordResult;
            }

            var classPropertyDeclaration = classDeclarationSyntax
                .DescendantNodes()
                .OfType<PropertyDeclarationSyntax>();

            var classResult = classPropertyDeclaration
                .Select(property => property.Identifier.Text)
                .ToList();

            return classResult;
        }

        private static List<string> GetFsProps(SemanticModel semanticModel, TypeSyntax type)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(type);

            if (symbolInfo.Symbol == null)
                return default;

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

        private static IEnumerable<StatementSyntax> GetAssertBlockSyntax(List<string> fromProps, List<string> toProps)
        {
            var result = new List<StatementSyntax>
            {
                GetAssertNotNullSyntax("tested"),
                GetAssertNotNullSyntax("expected"),
            };

            foreach (var from in fromProps)
            {
                var to = toProps.FirstOrDefault(toProp => toProp.Equals(from, StringComparison.OrdinalIgnoreCase))
                         ?? toProps.FirstOrDefault(toProp => toProp.IndexOf(from, StringComparison.OrdinalIgnoreCase) > -1
                                                             || from.IndexOf(toProp, StringComparison.OrdinalIgnoreCase) > -1);

                if (to == null)
                    to = "/*unexpected mismatch*/";
                else
                    toProps.Remove(to);

                result.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("Assert"),
                                    SyntaxFactory.IdentifierName("That")))
                            .WithArgumentList(
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                        new SyntaxNodeOrToken[]{
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

                                        })))));
            }

            if (toProps.Any())
            {
                foreach (var toProp in toProps)
                {
                    result.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("Assert"),
                                    SyntaxFactory.IdentifierName("That")))
                            .WithArgumentList(
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                        new SyntaxNodeOrToken[]{
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.IdentifierName("tested"),
                                                SyntaxFactory.IdentifierName(toProp))),
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
                                                                SyntaxFactory.IdentifierName("/*unexpected mismatch*/")))))))

                                        })))));
                }
            }

            return result;
        }

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
    }
}
