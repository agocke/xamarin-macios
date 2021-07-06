
namespace Mono.Cecil
{
	static class CecilExtensions
	{
		public static bool Is (this TypeReference t, string ns, string name)
		{
			return t.Namespace == ns && t.Name == name;
		}
	}
}