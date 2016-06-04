using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flame.Front;
using Flame.Front.Cli;
using Flame.Front.Projects;
using Pixie;

namespace ecsc
{
	public static class Program
	{
        private static Version LoycVersion
        {
            get
            {
                return typeof(LeMP.Compiler).Assembly.GetName().Version;
            }
        }

        private static MarkupNode FormattedLoycVersion
        {
            get
            {
                return new MarkupNode(NodeConstants.TextNodeType,
                    "EC# parser: Loyc " + LoycVersion.ToString(3));
            }
        }

		public static void Main(string[] args)
		{
			ProjectHandlers.RegisterHandler(new EcsProjectHandler());
			var compiler = new ConsoleCompiler(CompilerName.Create(
                "ecsc", "the EC# compiler", "https://github.com/jonathanvdc/ecsc/releases",
                new Lazy<IEnumerable<MarkupNode>>(() => new[] { FormattedLoycVersion })));
			Environment.Exit(compiler.Compile(args));
		}
	}
}
