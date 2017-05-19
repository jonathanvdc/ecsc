using Loyc.Ecs;
using Flame;
using Flame.Analysis;
using Flame.Binding;
using Flame.Compiler;
using Flame.Compiler.Projects;
using Flame.Compiler.Variables;
using Flame.Compiler.Visitors;
using Flame.DSProject;
using Flame.Ecs;
using Flame.Ecs.Diagnostics;
using Flame.Ecs.Passes;
using Flame.Ecs.Semantics;
using Flame.Front;
using Flame.Front.Options;
using Flame.Front.Projects;
using Flame.Front.Target;
using Flame.Verification;
using Flame.Syntax;
using LeMP;
using Loyc;
using Loyc.Collections;
using Loyc.Syntax;
using Loyc.Syntax.Les;
using Loyc.Syntax.Lexing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flame.Build;
using Pixie;
using Flame.Front.Passes;
using Flame.Build.Lazy;
using Flame.Ecs.Parsing;

namespace ecsc
{
    public class EcsProjectHandler : IProjectHandler
    {
        // Maps extensions to parsing services.
        private static readonly Dictionary<string, IParsingService> parsers =
            new Dictionary<string, IParsingService>(StringComparer.OrdinalIgnoreCase)
        {
            { "les", LesLanguageService.Value },
            { "les2", Les2LanguageService.Value },
            { "les3", Les3LanguageService.Value },
            { "ecs", EcsLanguageService.Value },
            { "cs", EcsLanguageService.WithPlainCSharpPrinter }
        };

        public IEnumerable<string> Extensions
        {
            get { return new string[] { "ecsproj" }.Concat(parsers.Keys); }
        }

        public ParsedProject Parse(ProjectPath Path, ICompilerLog Log)
        {
            if (Path.HasExtension("ecs") || Path.HasExtension("cs") || Path.HasExtension("les"))
            {
                return new ParsedProject(
                    new PathIdentifier(Path.Path.Name),
                    new SingleFileProject(Path, Log.Options.GetTargetPlatform()));
            }
            else
            {
                return new ParsedProject(
                    Path.Path,
                    DSProject.ReadProject(Path.Path.Path));
            }
        }

        public IProject MakeProject(IProject Project, ProjectPath Path, ICompilerLog Log)
        {
            var newPath = Path.Path.Parent.Combine(Project.Name).ChangeExtension("ecsproj");
            var dsp = DSProject.FromProject(Project, newPath.AbsolutePath.Path);
            dsp.WriteTo(newPath.Path);
            return dsp;
        }

        public async Task<IAssembly> CompileAsync(IProject Project, CompilationParameters Parameters)
        {
            var name = Parameters.Log.GetAssemblyName(Project.AssemblyName ?? Project.Name ?? "");
            var extBinder = await Parameters.BinderTask;

            INamespaceBranch mainNs = null;
            var asm = new LazyDescribedAssembly(new SimpleName(name), extBinder.Environment, descAsm =>
            {
                descAsm.AddNamespace(mainNs);
                descAsm.EntryPoint = EntryPointHelpers.InferEntryPoint(descAsm, Parameters.Log);
                AddAssemblyAttributes(descAsm, mainNs);
            });

            var asmBinder = new CachingBinder(new DualBinder(asm.CreateBinder(), extBinder));
            mainNs = await ParseCompilationUnitsAsync(Project.GetSourceItems(), Parameters, asmBinder, asm);

            return asm;
        }

        private static void AddAssemblyAttributes(LazyDescribedAssembly Assembly, INamespaceBranch Namespace)
        {
            if (Namespace == null)
            {
                return;
            }
            else if (Namespace is IMutableNamespace)
            {
                var mutNs = (IMutableNamespace)Namespace;
                Assembly.AddAttributes(mutNs.GetAssemblyAttributes());
            }
            foreach (var child in Namespace.Namespaces)
            {
                AddAssemblyAttributes(Assembly, child);
            }
        }

        private static async Task<INamespaceBranch> ParseCompilationUnitsAsync(
            List<IProjectSourceItem> SourceItems, CompilationParameters Parameters,
            IBinder Binder, IAssembly DeclaringAssembly)
        {
            var sink = new CompilerLogMessageSink(Parameters.Log, new SourceDocumentCache());
            var processor = new MacroProcessor(sink, typeof(LeMP.Prelude.BuiltinMacros));

            processor.AddMacros(typeof(LeMP.StandardMacros).Assembly, false);
            processor.AddMacros(typeof(EcscMacros.RequiredMacros).Assembly, false);

            var parsed = await ParseCompilationUnitsAsync(
                SourceItems, Parameters, processor, sink);
            return AnalyzeCompilationUnits(parsed, Binder, Parameters.Log, DeclaringAssembly);
        }

        private static IParsingService GetParser(string Identifier)
        {
            IParsingService result;
            if (parsers.TryGetValue(Identifier, out result))
                return result;
            else
                return EcsLanguageService.Value;
        }

        private static IParsingService GetParser(IProjectSourceItem SourceItem)
        {
            return GetParser(GetExtension(SourceItem.SourceIdentifier));
        }

        private static ILNodePrinter GetPrinter(string Identifier)
        {
            IParsingService result;
            if (parsers.TryGetValue(Identifier, out result))
                return (ILNodePrinter)result;
            else
                return EcsLanguageService.Value;
        }

        private static ILNodePrinter GetPrinter(IProjectSourceItem SourceItem)
        {
            return GetPrinter(GetExtension(SourceItem.SourceIdentifier));
        }

        private static string GetExtension(string Identifier)
        {
            return Enumerable.Last(Identifier.Split('.'));
        }

        private static ParsedDocument ParseCompilationUnit(
            IProjectSourceItem SourceItem, CompilationParameters Parameters,
            MacroProcessor Processor, CompilerLogMessageSink Sink)
        {
            Parameters.Log.LogEvent(new LogEntry("Status", "parsing '" + SourceItem.SourceIdentifier + "'"));

            // Retrieve the source code.
            var code = ProjectHandlerHelpers.GetSourceSafe(SourceItem, Parameters);
            if (code == null)
                return ParsedDocument.Empty;

            // Register, parse and expand macros.
            var parsedDoc = SourceHelpers.RegisterAndParse(code, GetParser(SourceItem), Sink)
                .ExpandMacros(Processor, EcscMacros.RequiredMacros.EcscPrologue);

            // Optionally print expanded code.
            if (Parameters.Log.Options.GetOption<bool>("E", false))
            {
                var outputService = GetPrinter(
                    Parameters.Log.Options.GetOption(
                        "syntax-format",
                        GetExtension(SourceItem.SourceIdentifier)));
                string newFile = parsedDoc.GetExpandedSource(
                    outputService, Sink, new LNodePrinterOptions()
                    { IndentString = new string(' ', 4) });
                Parameters.Log.LogMessage(new LogEntry("'" + SourceItem.SourceIdentifier + "' after macro expansion", Environment.NewLine + newFile));
            }

            Parameters.Log.LogEvent(new LogEntry("Status", "parsed '" + SourceItem.SourceIdentifier + "'"));

            return parsedDoc;
        }

        private static Task<IEnumerable<ParsedDocument>> ParseCompilationUnitsAsync(
            IReadOnlyList<IProjectSourceItem> SourceItems, CompilationParameters Parameters,
            MacroProcessor Processor, CompilerLogMessageSink Sink)
        {
            var units = new Task<ParsedDocument>[SourceItems.Count];
            for (int i = 0; i < units.Length; i++)
            {
                var item = SourceItems[i];
                units[i] = Task.Run(() =>
                    ParseCompilationUnit(
                        item, Parameters, Processor, Sink));
            }
            return Task.WhenAll(units).ContinueWith(t =>
                t.Result.Where(x => !x.IsEmpty));
        }

        private static INamespaceBranch AnalyzeCompilationUnits(
            IEnumerable<ParsedDocument> Units, IBinder Binder,
            ICompilerLog Log, IAssembly DeclaringAssembly)
        {
            var converter = NodeConverter.DefaultNodeConverter;
            NodeConverter.AddEnvironmentConverters(converter, Binder.Environment);
            var globalScope = new GlobalScope(
                Binder, new EcsConversionRules(Binder.Environment),
                Log, EcsTypeRenderer.Instance,
                Log.Options.GetDocumentationParser());

            var mainNs = new RootNamespace(DeclaringAssembly);
            foreach (var item in Units)
            {
                converter.ConvertCompilationUnit(globalScope, mainNs, item.Contents);
            }
            return mainNs;
        }

        private static void ParseCompilationUnit(
            IEnumerable<LNode> Nodes, GlobalScope Scope,
            IMutableNamespace DeclaringNamespace,
            NodeConverter Converter)
        {
            Converter.ConvertCompilationUnit(Scope, DeclaringNamespace, Nodes);
        }

        public IEnumerable<ParsedProject> Partition(IEnumerable<ParsedProject> Projects)
        {
            return new ParsedProject[]
            {
                new ParsedProject(
                    Projects.First().CurrentPath,
                    UnionProject.CreateUnion(Projects.Select(item => item.Project).ToArray()))
            };
        }

        public PassPreferences GetPassPreferences(ICompilerLog Log)
        {
            return new PassPreferences(new PassCondition[]
                {
                    new PassCondition(AutoInitializationPass.AutoInitializationPassName, _ => true),
                    new PassCondition(ValueTypeDelegateVisitor.ValueTypeDelegatePassName,
                        optInfo => ValueTypeDelegateVisitor.ValueTypeDelegateWarning.UseWarning(optInfo.Log.Options)),
                    new PassCondition(InfiniteRecursionPass.InfiniteRecursionPassName,
                        optInfo => InfiniteRecursionPass.IsUseful(optInfo.Log)),
                },
                new PassInfo<Tuple<IStatement, IMethod, ICompilerLog>, IStatement>[]
                {
                    new AtomicPassInfo<Tuple<IStatement, IMethod, ICompilerLog>, IStatement>(
                        AnalysisPasses.ValueTypeDelegatePass,
                        ValueTypeDelegateVisitor.ValueTypeDelegatePassName),

                    new AtomicPassInfo<Tuple<IStatement, IMethod, ICompilerLog>, IStatement>(
                        AutoInitializationPass.Instance,
                        AutoInitializationPass.AutoInitializationPassName),

                    new AtomicPassInfo<Tuple<IStatement, IMethod, ICompilerLog>, IStatement>(
                        InfiniteRecursionPass.Instance,
                        InfiniteRecursionPass.InfiniteRecursionPassName)
                });
        }
    }
}
