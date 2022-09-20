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

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

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
                return default;

            var nodeToChange = statement
                .Parent
                .Parent
                .Parent
                .Parent
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is ClassDeclarationSyntax)
                as ClassDeclarationSyntax;

            var genericName = ((GenericNameSyntax)statement.Parent)
                .Identifier
                .Text;

            var semanticModel = await document.GetSemanticModelAsync();

            if (statement.Arguments.Count != 2)
                return null;

            var (fromClass, fromProps) = await GetProps(semanticModel, statement.Arguments[0]);
            var (toClass, toProps) = await GetProps(semanticModel, statement.Arguments[1]);

            var newTree = SyntaxFactory.CompilationUnit()
                .WithMembers(
                    SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                        SyntaxFactory.ClassDeclaration(nodeToChange.Identifier.Text)
                            .WithModifiers(
                                SyntaxFactory.TokenList(
                                    SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
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
                                                                SyntaxFactory.IdentifierName(fromClass),
                                                                SyntaxFactory.Token(SyntaxKind.CommaToken),
                                                                SyntaxFactory.IdentifierName(toClass)
                                                            })))))))
                            .WithMembers(
                                SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                                    SyntaxFactory.MethodDeclaration(
                                            SyntaxFactory.PredefinedType(
                                                SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                                            SyntaxFactory.Identifier("TestScenario"))
                                        .WithAttributeLists(
                                            SyntaxFactory.SingletonList(
                                                SyntaxFactory.AttributeList(
                                                    SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Attribute(
                                                            SyntaxFactory.IdentifierName("Test"))))))
                                        .WithModifiers(
                                            SyntaxFactory.TokenList(
                                                SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                                        .WithParameterList(
                                            SyntaxFactory.ParameterList(
                                                SyntaxFactory.SeparatedList<ParameterSyntax>(
                                                    new SyntaxNodeOrToken[]
                                                    {
                                                        SyntaxFactory.Parameter(
                                                                SyntaxFactory.Identifier("expected"))
                                                            .WithType(
                                                                SyntaxFactory.IdentifierName(fromClass)),
                                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                                        SyntaxFactory.Parameter(
                                                                SyntaxFactory.Identifier("tested"))
                                                            .WithType(
                                                                SyntaxFactory.IdentifierName(toClass))
                                                    })))
                                        .WithBody(
                                            SyntaxFactory.Block(GetAssertBlockSyntax(fromProps, toProps)))))))
                .NormalizeWhitespace();

            var newNode = (await newTree
                    .SyntaxTree
                    .GetRootAsync())
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is ClassDeclarationSyntax);

            var editor = await DocumentEditor.CreateAsync(document);

            editor.ReplaceNode(nodeToChange, newNode);

            return editor.GetChangedDocument();
        }

        private static async Task<(string, List<string>)> GetProps(SemanticModel semanticModel, TypeSyntax type)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(type);

            var classDeclaration = await symbolInfo
                .Symbol
                .DeclaringSyntaxReferences
                .FirstOrDefault()
                .GetSyntaxAsync()
                as ClassDeclarationSyntax;

            if (classDeclaration == null)
                return default;

            var propertyDeclaration = classDeclaration
                .DescendantNodes()
                .OfType<PropertyDeclarationSyntax>();

            var result = propertyDeclaration
                .Select(property => property.Identifier.Text)
                .ToList();

            return (classDeclaration.Identifier.Text, result);
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
                var to = toProps.FirstOrDefault(x => x.Equals(from, StringComparison.OrdinalIgnoreCase))
                    ?? toProps.FirstOrDefault(t => t.IndexOf(from, StringComparison.OrdinalIgnoreCase) > -1
                                                || from.IndexOf(t, StringComparison.OrdinalIgnoreCase) > -1);

                toProps.Remove(to);

                if (to == null)
                    to = "/*unexpected mismatch*/";

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
                foreach (var to in toProps)
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
