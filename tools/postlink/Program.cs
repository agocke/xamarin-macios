using System;
using Mono.Cecil;
using Xamarin.Bundler;
using Xamarin.Linker;

namespace postlink
{
    class Program
    {
        static void Main(string[] args)
        {
            var target = new Target();
            var config = new LinkerConfiguration(target);
            foreach (var path in args)
            {
                var asmDef = AssemblyDefinition.ReadAssembly(path);
                target.Assemblies.Add(new Assembly(target, asmDef));
                config.FrameworkAssemblies.Add(path);
            }
            var extractStep = new ExtractBindingLibrariesStep(config);
            extractStep.TryEndProcess();
        }
    }
}
