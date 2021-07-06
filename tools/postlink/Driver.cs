
using System;
using Xamarin.Utils;

namespace Xamarin.Bundler {
	public partial class Driver {
		public static bool Force { get; set; }
		public static TargetFramework TargetFramework { get; set; }

		public static int Verbosity {
			get { return ErrorHelper.Verbosity; }
			set { ErrorHelper.Verbosity = value; }
		}

		public static void Log (int min_verbosity, string value)
		{
			if (min_verbosity > Verbosity)
				return;

			Console.WriteLine (value);
		}

		public static void Log (int min_verbosity, string format, params object [] args)
		{
			if (min_verbosity > Verbosity)
				return;

			if (args.Length > 0)
				Console.WriteLine (format, args);
			else
				Console.WriteLine (format);
		}

		internal static string GetFullPath ()
		{
			return System.Reflection.Assembly.GetExecutingAssembly ().Location;
		}
	}
}
