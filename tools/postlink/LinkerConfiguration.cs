using System.Collections.Generic;

namespace Xamarin.Bundler
{
	
}

namespace Xamarin.Linker {
	public class LinkerConfiguration {
	}
}

public class MSBuildItem {
	public string Include;
	public Dictionary<string, string> Metadata = new Dictionary<string, string> ();
}
