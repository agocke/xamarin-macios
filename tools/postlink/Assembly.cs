using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Mono.Cecil;
using ObjCRuntime;
using Xamarin.Utils;

namespace Xamarin.Bundler {

	struct NativeReferenceMetadata
	{
		public bool ForceLoad;
		public string Frameworks;
		public string WeakFrameworks;
		public string LibraryName;
		public string LinkerFlags;
		public LinkTarget LinkTarget;
		public bool NeedsGccExceptionHandling;
		public bool IsCxx;
		public bool SmartLink;

		// Optional
		public LinkWithAttribute Attribute;

		public NativeReferenceMetadata (LinkWithAttribute attribute)
		{
			ForceLoad = attribute.ForceLoad;
			Frameworks = attribute.Frameworks;
			WeakFrameworks = attribute.WeakFrameworks;
			LibraryName = attribute.LibraryName;
			LinkerFlags = attribute.LinkerFlags;
			LinkTarget = attribute.LinkTarget;
			NeedsGccExceptionHandling = attribute.NeedsGccExceptionHandling;
			IsCxx = attribute.IsCxx;
			SmartLink = attribute.SmartLink;
			Attribute = attribute;
		}
	}

	public partial class Assembly
	{
		public AssemblyBuildTarget BuildTarget;
		public string BuildTargetName;
		public bool IsCodeShared;
		public List<string> Satellites;
		public Application App { get { return Target.App; } }

		string full_path;
		bool? is_framework_assembly;

		public AssemblyDefinition AssemblyDefinition;
		public Target Target;
		public bool? IsFrameworkAssembly { get { return is_framework_assembly; } }
		public string FullPath {
			get {
				return full_path;
			}
			set {
				full_path = value;
				if (!is_framework_assembly.HasValue && !string.IsNullOrEmpty (full_path)) {
#if NET
					is_framework_assembly = Target.App.Configuration.FrameworkAssemblies.Contains (GetIdentity (full_path));
#else
					var real_full_path = Target.GetRealPath (full_path);
					is_framework_assembly = real_full_path.StartsWith (Path.GetDirectoryName (Path.GetDirectoryName (Target.Resolver.FrameworkDirectory)), StringComparison.Ordinal);
#endif
				}
			}
		}
		public string FileName { get { return Path.GetFileName (FullPath); } }
		public string Identity => GetIdentity (AssemblyDefinition);

		public static string GetIdentity (AssemblyDefinition ad)
		{
			if (!string.IsNullOrEmpty (ad.MainModule.FileName))
				return Path.GetFileNameWithoutExtension (ad.MainModule.FileName);
			return ad.Name.Name;
		}

		public static string GetIdentity (string path)
		{
			return Path.GetFileNameWithoutExtension (path);
		}

		public bool EnableCxx;
		public bool NeedsGccExceptionHandling;
		public bool ForceLoad;
		public HashSet<string> Frameworks = new HashSet<string> ();
		public HashSet<string> WeakFrameworks = new HashSet<string> ();
		public List<string> LinkerFlags = new List<string> (); // list of extra linker flags
		public List<string> LinkWith = new List<string>();
		public bool HasLinkWithAttributes { get; private set; }
		bool? symbols_loaded;
		List<string> link_with_resources; // a list of resources that must be removed from the app

		public Assembly (Target target, AssemblyDefinition definition)
		{
			this.Target = target;
			this.AssemblyDefinition = definition;
			this.FullPath = definition.MainModule.FileName;
		}

		public void LoadSymbols ()
		{
			if (symbols_loaded.HasValue)
				return;

			symbols_loaded = false;
			try {
				var pdb = Path.ChangeExtension (FullPath, ".pdb");
				if (File.Exists (pdb) || File.Exists (FullPath + ".mdb")) {
					AssemblyDefinition.MainModule.ReadSymbols ();
					symbols_loaded = true;
				}
			}
			catch {
				// do not let stale file crash us
				Driver.Log (3, "Invalid debugging symbols for {0} ignored", FullPath);
			}
		}
		void AddResourceToBeRemoved (string resource)
		{
			if (link_with_resources == null)
				link_with_resources = new List<string> ();
			link_with_resources.Add (resource);
		}


		public void ExtractNativeLinkInfo ()
		{
			// ignore framework assemblies, they won't have any LinkWith attributes
			if (IsFrameworkAssembly == true)
				return;

			var assembly = AssemblyDefinition;
			if (!assembly.HasCustomAttributes)
				return;

			string resourceBundlePath = Path.ChangeExtension (FullPath, ".resources");
			string manifestPath = Path.Combine (resourceBundlePath, "manifest");
			if (File.Exists (manifestPath)) {
				foreach (NativeReferenceMetadata metadata in ReadManifest (manifestPath)) {
					LogNativeReference (metadata);
					ProcessNativeReferenceOptions (metadata);

					switch (Path.GetExtension (metadata.LibraryName).ToLowerInvariant ()) {
					case ".framework":
						AssertiOSVersionSupportsUserFrameworks (metadata.LibraryName);
						Frameworks.Add (metadata.LibraryName);
#if MMP // HACK - MMP currently doesn't respect Frameworks on non-App - https://github.com/xamarin/xamarin-macios/issues/5203
						App.Frameworks.Add (metadata.LibraryName);
#endif
						break;
					case ".xcframework":
						// this is resolved, at msbuild time, into a framework
						// but we must ignore it here (can't be the `default` case)
						break;
					default:
#if MMP // HACK - MMP currently doesn't respect LinkWith - https://github.com/xamarin/xamarin-macios/issues/5203
						Driver.native_references.Add (metadata.LibraryName);
#endif
						LinkWith.Add (metadata.LibraryName);
						break;
					}
				}
			}

			ProcessLinkWithAttributes (assembly);

			// Make sure there are no duplicates between frameworks and weak frameworks.
			// Keep the weak ones.
			if (Frameworks != null && WeakFrameworks != null)
				Frameworks.ExceptWith (WeakFrameworks);

			if (NeedsGccExceptionHandling) {
				if (LinkerFlags == null)
					LinkerFlags = new List<string> ();
				LinkerFlags.Add ("-lgcc_eh");
			}

		}

		IEnumerable <NativeReferenceMetadata> ReadManifest (string manifestPath)
		{
			XmlDocument document = new XmlDocument ();
			document.LoadWithoutNetworkAccess (manifestPath);

			foreach (XmlNode referenceNode in document.GetElementsByTagName ("NativeReference")) {

				NativeReferenceMetadata metadata = new NativeReferenceMetadata ();
				metadata.LibraryName = Path.Combine (Path.GetDirectoryName (manifestPath), referenceNode.Attributes ["Name"].Value);

				var attributes = new Dictionary<string, string> ();
				foreach (XmlNode attribute in referenceNode.ChildNodes)
					attributes [attribute.Name] = attribute.InnerText;

				metadata.ForceLoad = ParseAttributeWithDefault (attributes ["ForceLoad"], false);
				metadata.Frameworks = attributes ["Frameworks"];
				metadata.WeakFrameworks = attributes ["WeakFrameworks"];
				metadata.LinkerFlags = attributes ["LinkerFlags"];
				metadata.NeedsGccExceptionHandling = ParseAttributeWithDefault (attributes ["NeedsGccExceptionHandling"], false);
				metadata.IsCxx = ParseAttributeWithDefault (attributes ["IsCxx"], false);
				metadata.SmartLink = ParseAttributeWithDefault (attributes ["SmartLink"], true);

				// TODO - The project attributes do not contain these bits, is that OK?
				//metadata.LinkTarget = (LinkTarget) Enum.Parse (typeof (LinkTarget), attributes ["LinkTarget"]);
				//metadata.Dlsym = (DlsymOption)Enum.Parse (typeof (DlsymOption), attributes ["Dlsym"]);
				yield return metadata;
			}
		}

		static bool ParseAttributeWithDefault (string attribute, bool defaultValue) => string.IsNullOrEmpty (attribute) ? defaultValue : bool.Parse (attribute);

		void ProcessLinkWithAttributes (AssemblyDefinition assembly)
		{
			//
			// Tasks:
			// * Remove LinkWith attribute: this is done in the linker.
			// * Remove embedded resources related to LinkWith attribute from assembly: this is done at a later stage,
			//   here we just compile a list of resources to remove.
			// * Extract embedded resources related to LinkWith attribute to a file
			// * Modify the linker flags used to build/link the dylib (if fastdev) or the main binary (if !fastdev)
			//

			for (int i = 0; i < assembly.CustomAttributes.Count; i++) {
				CustomAttribute attr = assembly.CustomAttributes [i];

				if (attr.Constructor == null)
					continue;

				TypeReference type = attr.Constructor.DeclaringType;
				if (!type.Is ("ObjCRuntime", "LinkWithAttribute"))
					continue;

				// Let the linker remove it the attribute from the assembly
				HasLinkWithAttributes = true;

				LinkWithAttribute linkWith = GetLinkWithAttribute (attr);
				NativeReferenceMetadata metadata = new NativeReferenceMetadata (linkWith);

				// If we've already processed this native library, skip it
				if (LinkWith.Any (x => Path.GetFileName (x) == metadata.LibraryName) || Frameworks.Any (x => Path.GetFileName (x) == metadata.LibraryName))
					continue;

				// Remove the resource from the assembly at a later stage.
				if (!string.IsNullOrEmpty (metadata.LibraryName))
					AddResourceToBeRemoved (metadata.LibraryName);

				ProcessNativeReferenceOptions (metadata);

				if (!string.IsNullOrEmpty (linkWith.LibraryName)) {
					switch (Path.GetExtension (linkWith.LibraryName).ToLowerInvariant ()) {
					case ".framework":
						AssertiOSVersionSupportsUserFrameworks (linkWith.LibraryName);
						Frameworks.Add (ExtractFramework (assembly, metadata));
						break;
					case ".xcframework":
						// this is resolved, at msbuild time, into a framework
						// but we must ignore it here (can't be the `default` case)
						break;
					default:
						LinkWith.Add (ExtractNativeLibrary (assembly, metadata));
						break;
					}
				}
			}
		}

		void AssertiOSVersionSupportsUserFrameworks (string path)
		{
			if (App.Platform == ApplePlatform.iOS && App.DeploymentTarget.Major < 8) {
				throw ErrorHelper.CreateError (1305, Errors.MT1305,
					FileName, Path.GetFileName (path), App.DeploymentTarget);
			}
		}

		void ProcessNativeReferenceOptions (NativeReferenceMetadata metadata)
		{
			// We can't add -dead_strip if there are any LinkWith attributes where smart linking is disabled.
			if (!metadata.SmartLink)
				App.DeadStrip = false;

			// Don't add -force_load if the binding's SmartLink value is set and the static registrar is being used.
			if (metadata.ForceLoad && !(metadata.SmartLink && App.Registrar == RegistrarMode.Static))
				ForceLoad = true;

			if (!string.IsNullOrEmpty (metadata.LinkerFlags)) {
				if (LinkerFlags == null)
					LinkerFlags = new List<string> ();
				if (!StringUtils.TryParseArguments (metadata.LinkerFlags, out string [] args, out var ex))
					throw ErrorHelper.CreateError (148, ex, Errors.MX0148, metadata.LinkerFlags, metadata.LibraryName, FileName, ex.Message);
				LinkerFlags.AddRange (args);
			}

			if (!string.IsNullOrEmpty (metadata.Frameworks)) {
				foreach (var f in metadata.Frameworks.Split (new char [] { ' ' })) {
					if (Frameworks == null)
						Frameworks = new HashSet<string> ();
					Frameworks.Add (f);
				}
			}

			if (!string.IsNullOrEmpty (metadata.WeakFrameworks)) {
				foreach (var f in metadata.WeakFrameworks.Split (new char [] { ' ' })) {
					if (WeakFrameworks == null)
						WeakFrameworks = new HashSet<string> ();
					WeakFrameworks.Add (f);
				}
			}

			if (metadata.NeedsGccExceptionHandling)
				NeedsGccExceptionHandling = true;

			if (metadata.IsCxx)
				EnableCxx = true;

#if MONOTOUCH
			if (metadata.Dlsym != DlsymOption.Default)
				App.SetDlsymOption (FullPath, metadata.Dlsym == DlsymOption.Required);
#endif
		}

		string ExtractNativeLibrary (AssemblyDefinition assembly, NativeReferenceMetadata metadata)
		{
			string path = Path.Combine (App.Cache.Location, metadata.LibraryName);

			if (!Application.IsUptodate (FullPath, path)) {
				Application.ExtractResource (assembly.MainModule, metadata.LibraryName, path, false);
				Driver.Log (3, "Extracted third-party binding '{0}' from '{1}' to '{2}'", metadata.LibraryName, FullPath, path);
				LogNativeReference (metadata);
			} else {
				Driver.Log (3, "Target '{0}' is up-to-date.", path);
			}

			if (!File.Exists (path))
				ErrorHelper.Warning (1302, Errors.MT1302, metadata.LibraryName, path);

			return path;
		}

		string ExtractFramework (AssemblyDefinition assembly, NativeReferenceMetadata metadata)
		{
			string path = Path.Combine (App.Cache.Location, metadata.LibraryName);

			var zipPath = path + ".zip";
			if (!Application.IsUptodate (FullPath, zipPath)) {
				Application.ExtractResource (assembly.MainModule, metadata.LibraryName, zipPath, false);
				Driver.Log (3, "Extracted third-party framework '{0}' from '{1}' to '{2}'", metadata.LibraryName, FullPath, zipPath);
				LogNativeReference (metadata);
			} else {
				Driver.Log (3, "Target '{0}' is up-to-date.", path);
			}

			if (!File.Exists (zipPath)) {
				ErrorHelper.Warning (1302, Errors.MT1302, metadata.LibraryName, zipPath);
			} else {
				if (!Directory.Exists (path))
					Directory.CreateDirectory (path);

				if (Driver.RunCommand ("/usr/bin/unzip", "-u", "-o", "-d", path, zipPath) != 0)
					throw ErrorHelper.CreateError (1303, Errors.MT1303, metadata.LibraryName, zipPath);
			}

			return path;
		}

		static void LogNativeReference (NativeReferenceMetadata metadata)
		{
			Driver.Log (3, "    LibraryName: {0}", metadata.LibraryName);
			Driver.Log (3, "    From: {0}", metadata.Attribute != null ? "LinkWith" : "Binding Manifest");
			Driver.Log (3, "    ForceLoad: {0}", metadata.ForceLoad);
			Driver.Log (3, "    Frameworks: {0}", metadata.Frameworks);
			Driver.Log (3, "    IsCxx: {0}", metadata.IsCxx);
			Driver.Log (3, "    LinkerFlags: {0}", metadata.LinkerFlags);
			Driver.Log (3, "    LinkTarget: {0}", metadata.LinkTarget);
			Driver.Log (3, "    NeedsGccExceptionHandling: {0}", metadata.NeedsGccExceptionHandling);
			Driver.Log (3, "    SmartLink: {0}", metadata.SmartLink);
			Driver.Log (3, "    WeakFrameworks: {0}", metadata.WeakFrameworks);
		}

		public static LinkWithAttribute GetLinkWithAttribute (CustomAttribute attr)
		{
			LinkWithAttribute linkWith;

			var cargs = attr.ConstructorArguments;
			switch (cargs.Count) {
			case 3:
				linkWith = new LinkWithAttribute ((string) cargs [0].Value, (LinkTarget) cargs [1].Value, (string) cargs [2].Value);
				break;
			case 2:
				linkWith = new LinkWithAttribute ((string) cargs [0].Value, (LinkTarget) cargs [1].Value);
				break;
			case 0:
				linkWith = new LinkWithAttribute ();
				break;
			default:
			case 1:
				linkWith = new LinkWithAttribute ((string) cargs [0].Value);
				break;
			}

			foreach (var property in attr.Properties) {
				switch (property.Name) {
				case "NeedsGccExceptionHandling":
					linkWith.NeedsGccExceptionHandling = (bool) property.Argument.Value;
					break;
				case "WeakFrameworks":
					linkWith.WeakFrameworks = (string) property.Argument.Value;
					break;
				case "Frameworks":
					linkWith.Frameworks = (string) property.Argument.Value;
					break;
				case "LinkerFlags":
					linkWith.LinkerFlags = (string) property.Argument.Value;
					break;
				case "LinkTarget":
					linkWith.LinkTarget = (LinkTarget) property.Argument.Value;
					break;
				case "ForceLoad":
					linkWith.ForceLoad = (bool) property.Argument.Value;
					break;
				case "IsCxx":
					linkWith.IsCxx = (bool) property.Argument.Value;
					break;
				case "SmartLink":
					linkWith.SmartLink = (bool) property.Argument.Value;
					break;
				case "Dlsym":
					linkWith.Dlsym = (DlsymOption) property.Argument.Value;
					break;
				default:
					break;
				}
			}

			return linkWith;
		}
	}

	public sealed class NormalizedStringComparer : IEqualityComparer<string>
	{
		public static readonly NormalizedStringComparer OrdinalIgnoreCase = new NormalizedStringComparer (StringComparer.OrdinalIgnoreCase);

		StringComparer comparer;

		public NormalizedStringComparer (StringComparer comparer)
		{
			this.comparer = comparer;
		}

		public bool Equals (string x, string y)
		{
			// From what I gather it doesn't matter which normalization form
			// is used, but I chose Form D because HFS normalizes to Form D.
			if (x != null)
				x = x.Normalize (System.Text.NormalizationForm.FormD);
			if (y != null)
				y = y.Normalize (System.Text.NormalizationForm.FormD);
			return comparer.Equals (x, y);
		}

		public int GetHashCode (string obj)
		{
			return comparer.GetHashCode (obj?.Normalize (System.Text.NormalizationForm.FormD));
		}
	}

	public class AssemblyCollection : IEnumerable<Assembly>
	{
		Dictionary<string, Assembly> HashedAssemblies = new Dictionary<string, Assembly> (NormalizedStringComparer.OrdinalIgnoreCase);

		public void Add (Assembly assembly)
		{
			Assembly other;
			if (HashedAssemblies.TryGetValue (assembly.Identity, out other))
				throw ErrorHelper.CreateError (2018, Errors.MT2018, assembly.Identity, other.FullPath, assembly.FullPath);
			HashedAssemblies.Add (assembly.Identity, assembly);
		}

		public void AddRange (AssemblyCollection assemblies)
		{
			foreach (var a in assemblies)
				Add (a);
		}

		public int Count {
			get {
				return HashedAssemblies.Count;
			}
		}

		public IDictionary<string, Assembly> Hashed {
			get { return HashedAssemblies; }
		}

		public bool TryGetValue (string identity, out Assembly assembly)
		{
			return HashedAssemblies.TryGetValue (identity, out assembly);
		}

		public bool TryGetValue (AssemblyDefinition asm, out Assembly assembly)
		{
			return HashedAssemblies.TryGetValue (Assembly.GetIdentity (asm), out assembly);
		}

		public bool Contains (AssemblyDefinition asm)
		{
			return HashedAssemblies.ContainsKey (Assembly.GetIdentity (asm));
		}

		public bool ContainsKey (string identity)
		{
			return HashedAssemblies.ContainsKey (identity);
		}

		public void Remove (string identity)
		{
			HashedAssemblies.Remove (identity);
		}

		public void Remove (Assembly assembly)
		{
			Remove (assembly.Identity);
		}

		public Assembly this [string key] {
			get { return HashedAssemblies [key]; }
			set { HashedAssemblies [key] = value; }
		}

		public void Update (Target target, IEnumerable<AssemblyDefinition> assemblies)
		{
			// This function will remove any assemblies not in 'assemblies', and add any new assemblies.
			var current = new HashSet<string> (HashedAssemblies.Keys, HashedAssemblies.Comparer);
			foreach (var assembly in assemblies) {
				var identity = Assembly.GetIdentity (assembly);
				if (!current.Remove (identity)) {
					// new assembly
					var asm = new Assembly (target, assembly);
					Add (asm);
					Driver.Log (1, "The linker added the assembly '{0}' to '{1}' to satisfy a reference.", asm.Identity, target.App.Name);
				} else {
					this [identity].AssemblyDefinition = assembly;
				}
			}

			foreach (var removed in current) {
				Driver.Log (1, "The linker removed the assembly '{0}' from '{1}' since there is no more reference to it.", this [removed].Identity, target.App.Name);
				Remove (removed);
			}
		}

#region Interface implementations
		IEnumerator IEnumerable.GetEnumerator ()
		{
			return GetEnumerator ();
		}

		public IEnumerator<Assembly> GetEnumerator ()
		{
			return HashedAssemblies.Values.GetEnumerator ();
		}

#endregion
	}
}
