using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flame.Front;
using Flame.Front.Cli;
using Flame.Front.Projects;

namespace ecsc
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			ProjectHandlers.RegisterHandler(new EcsProjectHandler());
			var compiler = new ConsoleCompiler("ecsc", "the direct EC# compiler", "https://github.com/jonathanvdc/ecsc/releases");
			Environment.Exit(compiler.Compile(args));
		}
	}
}
