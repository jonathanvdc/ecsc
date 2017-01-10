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
using Flame.Ecs.Passes;
using Flame.Ecs.Semantics;
using Flame.Front;
using Flame.Front.Options;
using Flame.Front.Projects;
using Flame.Front.Target;
using Flame.Verification;
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

namespace ecsc
{
	public class EcsProjectHandler : IProjectHandler
	{
        public IEnumerable<string> Extensions
		{
			get { return new string[] { "ecsproj", "ecs", "cs", "les" }; }
		}

		public IProject Parse(ProjectPath Path, ICompilerLog Log)
		{
			if (Path.HasExtension("ecs") || Path.HasExtension("cs") || Path.HasExtension("les"))
			{
				return new SingleFileProject(Path, Log.Options.GetTargetPlatform());
			}
			else
			{
				return DSProject.ReadProject(Path.Path.Path);
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
			var asm = new DescribedAssembly(new SimpleName(name), extBinder.Environment);

			var asmBinder = new CachingBinder(new DualBinder(asm.CreateBinder(), extBinder));

            asm.AddNamespace(await ParseCompilationUnitsAsync(Project.GetSourceItems(), Parameters, asmBinder, asm));
			asm.EntryPoint = EntryPointHelpers.InferEntryPoint(asm, Parameters.Log);

			return asm;
		}

		public static Task<INamespaceBranch> ParseCompilationUnitsAsync(
			List<IProjectSourceItem> SourceItems, CompilationParameters Parameters,
			IBinder Binder, IAssembly DeclaringAssembly)
		{
			var converter = NodeConverter.DefaultNodeConverter;
			NodeConverter.AddEnvironmentConverters(converter, Binder.Environment);
            var sink = new CompilerLogMessageSink(Parameters.Log, new SourceDocumentCache());
            var processor = new MacroProcessor(sink, typeof(LeMP.Prelude.BuiltinMacros));

			processor.AddMacros(typeof(LeMP.StandardMacros).Assembly, false);
			processor.AddMacros(typeof(EcscMacros.RequiredMacros).Assembly, false);

			return ParseCompilationUnitsAsync(
				SourceItems, Parameters, Binder,
				DeclaringAssembly, converter, processor, sink);
		}

		public static Task<INamespaceBranch> ParseCompilationUnitsAsync(
			List<IProjectSourceItem> SourceItems, CompilationParameters Parameters,
			IBinder Binder, IAssembly DeclaringAssembly,
            NodeConverter Converter, MacroProcessor Processor, CompilerLogMessageSink Sink)
		{
			var units = new Task[SourceItems.Count];
            var declaringNs = new RootNamespace(DeclaringAssembly);
			for (int i = 0; i < units.Length; i++)
			{
				var item = SourceItems[i];
				units[i] = ParseCompilationUnitAsync(
                    item, Parameters, Binder, declaringNs,
					Converter, Processor, Sink);
			}
            return Task.WhenAll(units).ContinueWith<INamespaceBranch>(_ =>
            {
                return declaringNs;
            });
		}

        private static ILNodePrinter GetParsingService(ICompilerOptions Options, string Key, ILNodePrinter Default)
		{
			switch (Options.GetOption<string>(Key, "").ToLower())
			{
				case "les":
					return LesLanguageService.Value;
                case "les2":
                    return Les2LanguageService.Value;
                case "les3":
                    return Les3LanguageService.Value;
				case "ecs":
					return EcsLanguageService.Value;
				case "cs":
					return EcsLanguageService.WithPlainCSharpPrinter;
				default:
					return Default;
			}
		}

		public static Task ParseCompilationUnitAsync(
			IProjectSourceItem SourceItem, CompilationParameters Parameters, IBinder Binder,
            IMutableNamespace DeclaringNamespace, NodeConverter Converter, MacroProcessor Processor, CompilerLogMessageSink Sink)
		{
			Parameters.Log.LogEvent(new LogEntry("Status", "Parsing " + SourceItem.SourceIdentifier));
            Action doParse = () =>
				{
					var code = ProjectHandlerHelpers.GetSourceSafe(SourceItem, Parameters);
					if (code == null)
						return;

                    Sink.DocumentCache.Add(code);

					var globalScope = new GlobalScope(Binder, EcsConversionRules.Instance, Parameters.Log, EcsTypeNamer.Instance);
					bool isLes = Enumerable.Last(SourceItem.SourceIdentifier.Split('.')).Equals("les", StringComparison.OrdinalIgnoreCase);
					var service = isLes ? (IParsingService)LesLanguageService.Value : EcsLanguageService.Value;
					var nodes = ParseNodes(code.Source, SourceItem.SourceIdentifier, service, Processor, Sink);

					if (Parameters.Log.Options.GetOption<bool>("E", false))
					{
                        var outputService = GetParsingService(
                            Parameters.Log.Options, "syntax-format", 
                            isLes ? (ILNodePrinter)LesLanguageService.Value : EcsLanguageService.Value);
                        string newFile = outputService.Print(
                            nodes, Sink, options: new LNodePrinterOptions() 
                            { IndentString = new string(' ', 4) });
						Parameters.Log.LogMessage(new LogEntry("'" + SourceItem.SourceIdentifier + "' after macro expansion", Environment.NewLine + newFile));
					}

                    ParseCompilationUnit(nodes, globalScope, DeclaringNamespace, Converter);
					Parameters.Log.LogEvent(new LogEntry("Status", "Parsed " + SourceItem.SourceIdentifier));
                };
            // TODO: re-enable this when race condition bug in Loyc is solved
            // return Task.Run(doParse);
            doParse();
            return Task.FromResult(true);
		}

		public static IEnumerable<LNode> ParseNodes(
			string Text, string Identifier, IParsingService Service,
			MacroProcessor Processor, IMessageSink Sink)
		{
			var lexer = Service.Tokenize(new UString(Text), Identifier, Sink);

			var nodes = Service.Parse(lexer, Sink);

            return Processor.ProcessSynchronously(new VList<LNode>(
                new LNode[] {  EcscMacros.RequiredMacros.EcscPrologue }.Concat(
                    nodes)));
		}

		public static void ParseCompilationUnit(
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
