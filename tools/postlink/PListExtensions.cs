
using System.IO;
using System.Xml;

namespace Xamarin
{
	static class PListExtensions
	{
		public static void LoadWithoutNetworkAccess (this XmlDocument doc, string filename)
		{
			using (var fs = new FileStream (filename, FileMode.Open, FileAccess.Read)) {
				var settings = new XmlReaderSettings ()
				{
					XmlResolver = null,
					DtdProcessing = DtdProcessing.Parse,
				};
				using (var reader = XmlReader.Create (fs, settings)) {
					doc.Load (reader);
				}
			}
		}
	}
}