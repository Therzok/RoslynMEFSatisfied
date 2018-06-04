using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.Composition;
using Mono.Addins;
using MonoDevelop.Core;
using MonoDevelop.Core.AddIns;

namespace MEFSatisfied
{
	public static class MEFCatalog
	{

		public class ExportDefinition : IEquatable<ExportDefinition>
		{
			public string TypeName { get; }
			public string AssemblyName { get; }
			public string Language { get; }
			public string Layer { get; }

			public ExportDefinition (string name, string assemblyName, string language, string layer)
			{
				TypeName = name;
				AssemblyName = assemblyName;
				Language = language;
				Layer = layer;
			}

			public bool Equals(ExportDefinition other) => TypeName == other.TypeName && AssemblyName == other.AssemblyName;
			public override int GetHashCode() => TypeName.GetHashCode() ^ AssemblyName.GetHashCode();
		}

		public static async Task AnalyzeCatalog(HashSet<Assembly> assemblies = null)
		{
			assemblies = assemblies ?? ReadAssembliesFromAddins();
			var catalog = await CreateCatalog(assemblies);

			Dictionary<string, HashSet<ExportDefinition>> map = new Dictionary<string, HashSet<ExportDefinition>>();

			// Go once through all the parts to setup all exports
			foreach (var part in catalog.Parts)
			{
				foreach (var kvp in part.ExportDefinitions)
				{
					var type = kvp.Key.Type ?? part.TypeRef;
					var export = kvp.Value;

					var contractName = export.ContractName;
					string language = null;
					string layer = null;

					foreach (var exportedType in part.ExportedTypes) {
						string metadataKey;
						string layerKey = null;
						string languageKey = null;

						switch (exportedType.ContractName)
						{
							case "Microsoft.CodeAnalysis.Host.ILanguageService":
							case "Microsoft.CodeAnalysis.Host.Mef.ILanguageServiceFactory":
								languageKey = "Language";
								metadataKey = "ServiceType";
								layerKey = "Layer";
								break;

							case "Microsoft.CodeAnalysis.Host.IWorkspaceService":
							case "Microsoft.CodeAnalysis.Host.Mef.IWorkspaceServiceFactory":
								metadataKey = "ServiceType";
								layerKey = "Layer";
								break;

							case "Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider":
							case "Microsoft.CodeAnalysis.CodeRefactorings.CodeRefactoringProvider":
								goto skip;
							default:
								continue;
						}

						if (exportedType.Metadata.TryGetValue(metadataKey, out var serviceType))
						{
							var serviceName = (string)serviceType;
							contractName = serviceName.Substring(0, serviceName.IndexOf(','));
						}

						if (layerKey != null && exportedType.Metadata.TryGetValue(layerKey, out var layerType))
							layer = (string)layerType;

						if (languageKey != null && exportedType.Metadata.TryGetValue(languageKey, out var languageType))
							language = (string)languageType;
					}

					if (!map.TryGetValue(contractName, out var exportDefinitions)) {
						map[contractName] = exportDefinitions = new HashSet<ExportDefinition>();
					}

					exportDefinitions.Add(new ExportDefinition(type.FullName, type.AssemblyName.FullName, language, layer));

				skip:
					// Empty statement here.
					;
				}
			}

			// Report all imports which are definitely not satisfied
			var notSatisfied = new HashSet<string>();
			foreach (var part in catalog.Parts)
			{
				foreach (var import in part.Imports)
				{
					if (!map.TryGetValue(import.ImportDefinition.ContractName, out var def))
					{
						notSatisfied.Add(import.ImportDefinition.ContractName);
					}
				}
			}

			if (notSatisfied.Count > 0)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Not satisfied:");
				Console.ResetColor();
				foreach (var part in notSatisfied)
				{
					Console.WriteLine(part);
				}

				Console.WriteLine();
			}

			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Unimplemented roslyn services:");
			Console.ResetColor();

			var workspaceServiceType = typeof(IWorkspaceService);
			var languageServiceType = typeof(ILanguageService);
			foreach (var type in assemblies.SelectMany(x => x.DefinedTypes))
			{
				if (!type.IsInterface)
					continue;

				var allInterfaces = type.GetInterfaces();
				var minimalInterfaces = allInterfaces
					.Where(x => !allInterfaces.Any(t => t.GetInterfaces().Contains (x)));

				if (minimalInterfaces.Contains (workspaceServiceType) || minimalInterfaces.Contains (languageServiceType))
				{
					if (!map.TryGetValue(type.FullName, out var exports) || exports.All(x => IsStubImplementation(x.TypeName))) {
						Console.WriteLine(type.FullName);
					}
				}
			}

			Console.WriteLine();

			const bool verbose = true;
			if (verbose)
			{
				foreach (var item in map)
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine(item.Key);
					Console.ResetColor();
					foreach (var export in item.Value)
					{
						if (export.Layer != null)
							Console.Write("{0}\t", export.Layer);

						if (export.Language != null)
							Console.Write("{0}\t", export.Language);

						Console.WriteLine(export.TypeName);
					}
				}
			}
		}

		static bool IsStubImplementation(string name)
		{
			return name.IndexOf("Default", StringComparison.OrdinalIgnoreCase) != -1 || name.IndexOf("Null", StringComparison.OrdinalIgnoreCase) != -1 ||
					   name.IndexOf("NoOp", StringComparison.OrdinalIgnoreCase) != -1;
		}

		public static async Task<ComposableCatalog> CreateCatalog (HashSet<Assembly> assemblies = null)
		{
			Resolver StandardResolver = Resolver.DefaultInstance;
			PartDiscovery Discovery = PartDiscovery.Combine(
				new AttributedPartDiscoveryV1(StandardResolver),
				new AttributedPartDiscovery(StandardResolver, true));

			var parts = await Discovery.CreatePartsAsync(assemblies ?? ReadAssembliesFromAddins ());

			ComposableCatalog catalog = ComposableCatalog.Create(StandardResolver)
				.WithCompositionService()
				.WithDesktopSupport()
				.AddParts(parts);

			return catalog;

		}

		static HashSet<Assembly> ReadAssembliesFromAddins()
		{
			var assemblies = new HashSet<Assembly>();
			ReadAssemblies(assemblies, "/MonoDevelop/Ide/TypeService/PlatformMefHostServices");
			ReadAssemblies(assemblies, "/MonoDevelop/Ide/TypeService/MefHostServices");
			ReadAssemblies(assemblies, "/MonoDevelop/Ide/Composition");
			return assemblies;
		}

		static void ReadAssemblies(HashSet<Assembly> assemblies, string extensionPath)
		{
			foreach (var node in AddinManager.GetExtensionNodes(extensionPath)) {
				if (node is AssemblyExtensionNode assemblyNode) {
					try {
						string id = assemblyNode.Addin.Id;
						string assemblyName = assemblyNode.FileName;
						// Make sure the add-in that registered the assembly is loaded, since it can bring other
						// other assemblies required to load this one
						AddinManager.LoadAddin(null, id);

						var assemblyFilePath = assemblyNode.Addin.GetFilePath(assemblyNode.FileName);
						var assembly = Runtime.SystemAssemblyService.LoadAssemblyFrom(assemblyFilePath);
						assemblies.Add(assembly);
					} catch (Exception e) {
						LoggingService.LogError("Composition can't load assembly: " + assemblyNode.FileName, e);
					}
				}
			}
		}
	}
}
