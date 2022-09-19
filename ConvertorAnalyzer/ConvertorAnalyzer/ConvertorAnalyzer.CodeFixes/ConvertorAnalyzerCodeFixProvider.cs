using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConvertorAnalyzer
{
    public class ConvertFrom
    {
        public int FromIntProp { get; set; }
        public string FromStringProp { get; set; } = "StringProp";
    }

    public class ConvertTo
    {
        public int ToIntProp { get; set; }
        public string ToStringProp { get; set; } = "StringProp";
    }

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

            var codeBlock = (BlockSyntax)root.DescendantNodes()
                .FirstOrDefault(node => node is BlockSyntax);

            var argumentsNode = (TypeArgumentListSyntax)statement.DescendantNodes()
                .FirstOrDefault(node => node is TypeArgumentListSyntax);

            if (argumentsNode == null)
                return default;

            var arguments = argumentsNode.Arguments
                .GetWithSeparators()
                .Where(node => node.IsNode)
                .Select(node => Type.GetType("ConvertorAnalyzer." + node))
                .ToList();

            if (arguments.Count < 2)
                return null;

            var result = new StringBuilder(128);

            var fromProps = arguments[0].GetProperties();
            var toProps = arguments[1].GetProperties();

            result.AppendLine("{");
            result.AppendLine("\t[Test]");
            result.AppendLine("\tpublic void TestCase()");
            result.AppendLine("\t{");

            for (int i = 0; i < arguments.Count; i++)
            {
                result.AppendLine($"\t\tAssert.That(tested.{toProps[i].Name}, Is.EqualTo({fromProps[i].Name});");
            }

            result.AppendLine("\t}");
            result.AppendLine("}");

            var newTree = CSharpSyntaxTree.ParseText(result.ToString());

            var newNode = newTree
                .GetRoot()
                .DescendantNodesAndSelf()
                .FirstOrDefault(node => node is BlockSyntax);

            var editor = await DocumentEditor.CreateAsync(document);

            editor.ReplaceNode(codeBlock, newNode);

            return editor.GetChangedDocument();
        }
    }
}
