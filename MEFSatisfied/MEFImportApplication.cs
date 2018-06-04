using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Composition;
using MonoDevelop.Core;
using MonoDevelop.Ide.Composition;

namespace MEFSatisfied
{
	class MEFImportApplication : IApplication
	{
		Dictionary<string, List<string>> exportsPerAssembly = new Dictionary<string, List<string>>();

		public async Task<int> Run(string[] arguments)
		{
			if (arguments.Length == 0)
			{
				Console.WriteLine("Usage: mdtool mef-graph [addin-ids]");
				return 1;
			}

			foreach (var arg in arguments) {
				Mono.Addins.AddinManager.LoadAddin(new Mono.Addins.ConsoleProgressStatus(false), arg);
			}

			await MEFCatalog.AnalyzeCatalog();
			return 0;
		}
	}
}
