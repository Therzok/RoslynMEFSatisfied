using System;
using MonoDevelop.Components.Commands;

namespace MEFSatisfied
{
	enum Commands
	{
		Dump,
	}

	class DumpHandler : CommandHandler
	{
		protected override async void Run()
		{
			await MEFCatalog.AnalyzeCatalog();
		}
	}
}
