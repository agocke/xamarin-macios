
using System;
using Mono.Cecil;
using ObjCRuntime;

namespace Registrar
{
	class BindAsAttribute : Attribute
	{
		public BindAsAttribute (TypeReference type)
		{
			this.Type = type;
		}

		public TypeReference Type { get; set; }
		public TypeReference OriginalType { get; set; }
	}

	class CategoryAttribute : Attribute {
		public CategoryAttribute (TypeDefinition type)
		{
			Type = type;
		}

		public TypeDefinition Type { get; set; }
		public string Name { get; set; }
	}

	class ProtocolAttribute : Attribute {
		public TypeDefinition WrapperType { get; set; }
		public string Name { get; set; }
		public bool IsInformal { get; set; }
		public Version FormalSinceVersion { get; set; }
	}

	class RegisterAttribute : Attribute {
		public RegisterAttribute () { }
		public RegisterAttribute (string name)
		{
			this.Name = name;
		}

		public RegisterAttribute (string name, bool isWrapper)
		{
			this.Name = name;
			this.IsWrapper = isWrapper;
		}

		public string Name { get; set; }
		public bool IsWrapper { get; set; }
		public bool SkipRegistration { get; set; }
	}

	public sealed class ProtocolMemberAttribute : Attribute {
		public ProtocolMemberAttribute () {}

		public bool IsRequired { get; set; }
		public bool IsProperty { get; set; }
		public bool IsStatic { get; set; }
		public string Name { get; set; }
		public string Selector { get; set; }
		public TypeReference ReturnType { get; set; }
		public TypeReference ReturnTypeDelegateProxy { get; set; }
		public TypeReference[] ParameterType { get; set; }
		public bool[] ParameterByRef { get; set; }
		public TypeReference [] ParameterBlockProxy { get; set; }
		public bool IsVariadic { get; set; }

		public TypeReference PropertyType { get; set; }
		public string GetterSelector { get; set; }
		public string SetterSelector { get; set; }
		public ArgumentSemantic ArgumentSemantic { get; set; }

		public MethodDefinition Method { get; set; } // not in the API, used to find the original method in the static registrar
	}

	class AdoptsAttribute : Attribute
	{
		public string ProtocolType { get; set; }
	}

}