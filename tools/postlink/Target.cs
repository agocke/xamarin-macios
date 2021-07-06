
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

namespace Xamarin.Bundler {
	public partial class Target {
		public Application App;
		public AssemblyCollection Assemblies = new AssemblyCollection (); // The root assembly is not in this list.

		public void ExtractNativeLinkInfo (List<Exception> exceptions)
		{
			foreach (var a in Assemblies) {
				try {
					a.ExtractNativeLinkInfo ();
				} catch (Exception e) {
					exceptions.Add (e);
				}
			}

#if MTOUCH
			if (!App.OnlyStaticLibraries && Assemblies.Count ((v) => v.HasLinkWithAttributes) > 1) {
				ErrorHelper.Warning (127, Errors.MT0127);
				App.ClearAssemblyBuildTargets (); // the default is to compile to static libraries, so just revert to the default.
			}
#endif
		}

		// This is to load the symbols for all assemblies, so that we can give better error messages
		// (with file name / line number information).
		public void LoadSymbols ()
		{
			foreach (var a in Assemblies)
				a.LoadSymbols ();
		}

		public List<Target> Targets = new List<Target> ();

		[DllImport (Constants.libSystemLibrary, SetLastError = true)]
		static extern string realpath (string path, IntPtr zero);

		public static string GetRealPath (string path, bool warnIfNoSuchPathExists = true)
		{
			// For some reason realpath doesn't always like filenames only, and will randomly fail.
			// Prepend the current directory if there's no directory specified.
			if (string.IsNullOrEmpty (Path.GetDirectoryName (path)))
				path = Path.Combine (Environment.CurrentDirectory, path);

			var rv = realpath (path, IntPtr.Zero);
			if (rv != null)
				return rv;

			var errno = Marshal.GetLastWin32Error ();
			if (warnIfNoSuchPathExists || (errno !=2))
				ErrorHelper.Warning (54, Errors.MT0054, path, FileCopier.strerror (errno), errno);
			return path;
		}
	}
}
