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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ConvertorAnalyzer
{
    //public class ConvertFrom
    //{
    //    public int FromIntProp { get; set; }
    //    public string FromStringProp { get; set; } = "StringProp";
    //}

    //public class ConvertTo
    //{
    //    public int ToIntProp { get; set; }
    //    public string ToStringProp { get; set; } = "StringProp";
    //}

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
            var statement = root.FindNode(diagnostic.Location.SourceSpan);

            var argumentsNode = statement.DescendantNodes()
                .FirstOrDefault(node => node is TypeArgumentListSyntax)
                as TypeArgumentListSyntax;

            var nodeToChange = statement
                .Parent
                .Parent
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is ClassDeclarationSyntax) 
                as ClassDeclarationSyntax;

            if (argumentsNode == null || nodeToChange == null)
                return default;

            var semanticModel = await document.GetSemanticModelAsync();

            if (argumentsNode.Arguments.Count != 2)
                return null;

            var (fromClass, fromProps) = await GetProps(semanticModel, argumentsNode.Arguments[0]);
            var (toClass, toProps) = await GetProps(semanticModel, argumentsNode.Arguments[1]);

            var result = new StringBuilder(128);

            var existedCode = nodeToChange.GetText().ToString().TrimEnd('}');

            result.Append(existedCode);
            result.AppendLine("\t[Test]");
            result.AppendLine($"\tpublic void TestCase({fromClass} expected, {toClass} tested)");
            result.AppendLine("\t{");

            for (int i = 0; i < fromProps.Count; i++)
            {
                result.AppendLine($"\t\tAssert.That(tested.{toProps[i]}, Is.EqualTo(expected.{fromProps[i]});");
            }

            result.AppendLine("\t}");
            result.AppendLine("}");

            var newTree = CSharpSyntaxTree.ParseText(result.ToString());

            var newNode = newTree
                .GetRoot()
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
    }
}
