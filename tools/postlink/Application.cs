
using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Xamarin.Linker;
using Xamarin.Utils;

namespace Xamarin.Bundler
{

	[Flags]
	public enum RegistrarOptions {
		Default = 0,
		Trace = 1,
	}

	public enum RegistrarMode {
		Default,
		Dynamic,
		PartialStatic,
		Static,
	}
	public class Application {
		public Cache Cache;
		public string AppDirectory = ".";
		public LinkerConfiguration Configuration;
		public ApplePlatform Platform { get { return Driver.TargetFramework.Platform; } }
		public RegistrarMode Registrar = RegistrarMode.Default;
		public bool DeadStrip = true;

		// This is just a name for this app to show in log/error messages, etc.
		public string Name {
			get { return Path.GetFileNameWithoutExtension (AppDirectory); }
		}
		public Version DeploymentTarget;

		public string PlatformName {
			get {
				switch (Platform) {
				case ApplePlatform.iOS:
					return "iOS";
				case ApplePlatform.TVOS:
					return "tvOS";
				case ApplePlatform.WatchOS:
					return "watchOS";
				case ApplePlatform.MacOSX:
					return "macOS";
				case ApplePlatform.MacCatalyst:
					return "MacCatalyst";
				default:
					throw new NotImplementedException ();
				}
			}
		}

		public List<Target> Targets = new List<Target> ();

		// This is to load the symbols for all assemblies, so that we can give better error messages
		// (with file name / line number information).
		public void LoadSymbols ()
		{
			foreach (var t in Targets)
				t.LoadSymbols ();
		}

		public static bool IsUptodate (string source, string target, bool check_contents = false, bool check_stamp = true)
		{
			return FileCopier.IsUptodate (source, target, check_contents, check_stamp);
		}

		public static void ExtractResource (ModuleDefinition module, string name, string path, bool remove)
		{
			for (int i = 0; i < module.Resources.Count; i++) {
				EmbeddedResource embedded = module.Resources[i] as EmbeddedResource;

				if (embedded == null || embedded.Name != name)
					continue;

				string dirname = Path.GetDirectoryName (path);
				if (!Directory.Exists (dirname))
					Directory.CreateDirectory (dirname);

				using (Stream ostream = File.OpenWrite (path)) {
					embedded.GetResourceStream ().CopyTo (ostream);
				}

				if (remove)
					module.Resources.RemoveAt (i);

				break;
			}
		}
	}
}