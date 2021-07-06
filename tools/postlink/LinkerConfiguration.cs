using System.Collections.Generic;
using Xamarin.Bundler;

namespace Xamarin.Linker {
	public class LinkerConfiguration {
		public LinkerConfiguration(Target t)
		{
			this.Target = t;
		}

		public Target Target { get; private set; }
		public HashSet<string> FrameworkAssemblies { get; private set; } = new HashSet<string> ();

		Dictionary<string, List<MSBuildItem>> msbuild_items = new Dictionary<string, List<MSBuildItem>> ();

		public void WriteOutputForMSBuild (string itemName, List<MSBuildItem> items)
		{
			if (!msbuild_items.TryGetValue (itemName, out var list)) {
				msbuild_items [itemName] = items;
			} else {
				list.AddRange (items);
			}
		}
	}
}

public class MSBuildItem {
	public string Include;
	public Dictionary<string, string> Metadata = new Dictionary<string, string> ();
}
