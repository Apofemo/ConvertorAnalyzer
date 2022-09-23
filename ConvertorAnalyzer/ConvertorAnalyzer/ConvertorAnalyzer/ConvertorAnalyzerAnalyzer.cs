using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace ConvertorAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ConvertorAnalyzerAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ConvertorAnalyzer";

        public const string MethodName = "TestScenario";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Naming";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxTreeAction(syntaxTreeContext =>
            {
                var root = syntaxTreeContext.Tree.GetRoot(syntaxTreeContext.CancellationToken);

                foreach (var statement in root.DescendantNodes().OfType<GenericNameSyntax>())
                {
                    var argumentsNode = statement.DescendantNodes()
                            .FirstOrDefault(node => node is TypeArgumentListSyntax)
                            as TypeArgumentListSyntax;

                    if (argumentsNode == null || argumentsNode.Arguments.Count != 2)
                        continue;

                    var genericName = statement
                        .Identifier
                        .Text
                        .ToLower();

                    if (!genericName.Contains("converter"))
                        continue;

                    var parentNode = statement
                            .Parent?
                            .Parent?
                            .Parent?
                            .DescendantNodesAndSelf()
                            .FirstOrDefault(node => node is ClassDeclarationSyntax)
                        as ClassDeclarationSyntax;

                    if (parentNode == null)
                        continue;

                    var hasCreatedMethod = parentNode.Members
                        .Where(member => member is MethodDeclarationSyntax)
                        .Select(method => ((MethodDeclarationSyntax)method).Identifier.ToString())
                        .Any(name => name == MethodName);

                    if (hasCreatedMethod)
                        continue;

                    var diagnostic = Diagnostic.Create(Rule, argumentsNode.GetLocation());
                    syntaxTreeContext.ReportDiagnostic(diagnostic);
                }
            });
        }
    }
}
