using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Options;

namespace CabbageCrow.AssemblyPublicizer
{
    /// <summary>
    /// Creates a copy of an assembly in which all members are public (types, methods, fields, getters and setters of properties).
    /// If you use the modified assembly as your reference and compile your dll with the option "Allow unsafe code" enabled, 
    /// you can access all private elements even when using the original assembly.
    /// Without "Allow unsafe code" you get an access violation exception during runtime when accessing private members except for types.  
    /// How to enable it: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/unsafe-compiler-option
    /// arg 0 / -i|--input:		Path to the assembly (absolute or relative)
    /// arg 1 / -o|--output:	[Optional] Output path/filename
    ///							Can be just a (relative) path like "subdir1\subdir2"
    ///							Can be just a filename like "CustomFileName.dll"
    ///							Can be a filename with path like "C:\subdir1\subdir2\CustomFileName.dll"
    /// </summary>
    class AssemblyPublicizer
    {
        static bool automaticExit, help;

        static void Main(string[] args)
        {
            var suffix = "_publicized";
            var defaultOutputDir = "publicized_assemblies";

            var inputs = new List<string>();
            string outputDir = "";

            var options = new OptionSet
            {
                { "i|input=", "Paths (relative or absolute) to the input assemblies, separated by comma", i => inputs = i.Split(',').ToList() },
                { "o|output=", "[Optional] Output directory for the modified assemblies", o => outputDir = o },
                { "e|exit", "Application should automatically exit", e => automaticExit = e != null },
                { "h|help", "Show this message", h => help = h != null }
            };

            try
            {
                var extra = options.Parse(args);
                if (help)
                    ShowHelp(options);

                // Fallback to positional arguments if not using flags
                if (!inputs.Any() && extra.Count >= 1)
                {
                    inputs = extra.Take(extra.Count - 1).ToList();
                    outputDir = extra.LastOrDefault();
                }

                // If no inputs are specified, load all assemblies in the current directory
                if (!inputs.Any())
                {
                    var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    inputs = Directory.GetFiles(currentDirectory, "*.dll").ToList(); // assuming .dll files, change extension if necessary
                }
                /*if (!inputs.Any())
                    throw new OptionException("No input files specified.", "input");*/

                // If no output directory specified, use default
                if (string.IsNullOrEmpty(outputDir))
                {
                    outputDir = defaultOutputDir;
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine($"ERROR! {e.Message}");
                Console.WriteLine("Try `--help` for more information.");
                Exit(10);
            }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            foreach (var inputFile in inputs)
            {
                ProcessAssembly(inputFile, outputDir, suffix);
            }

            Exit(0);
        }

        static void ProcessAssembly(string inputFile, string outputDir, string suffix)
        {
            Console.WriteLine($"Processing: {inputFile}");

            if (!File.Exists(inputFile))
            {
                Console.WriteLine("ERROR! File doesn't exist or you don't have sufficient permissions.");
                return;
            }

            AssemblyDefinition assembly;
            try
            {
                assembly = AssemblyDefinition.ReadAssembly(inputFile);
            }
            catch (Exception)
            {
                Console.WriteLine("ERROR! Cannot read the assembly. Please check your permissions.");
                return;
            }

            var allTypes = GetAllTypes(assembly.MainModule);
            var allMethods = allTypes.SelectMany(t => t.Methods);
            var allFields = allTypes.SelectMany(t => t.Fields);

            MakePublic(allTypes, allMethods, allFields);

            var outputName = $"{Path.GetFileNameWithoutExtension(inputFile)}{suffix}{Path.GetExtension(inputFile)}";
            var outputFile = Path.Combine(outputDir, outputName);
            try
            {
                assembly.Write(outputFile);
                Console.WriteLine($"Saved: {outputFile}");
            }
            catch (Exception)
            {
                Console.WriteLine("ERROR! Cannot create/overwrite the new assembly.");
            }
        }

        static void MakePublic(IEnumerable<TypeDefinition> types, IEnumerable<MethodDefinition> methods, IEnumerable<FieldDefinition> fields)
        {
            Console.WriteLine($"Making types, methods, and fields public...");

            int count = types.Count(t => !t.IsPublic && !t.IsNestedPublic);
            foreach (var type in types)
            {
                type.IsPublic = true;
                type.IsNestedPublic = true;
            }

            Console.WriteLine($"Changed {count} types to public.");

            count = methods.Count(m => !m.IsPublic);
            foreach (var method in methods)
            {
                method.IsPublic = true;
            }

            Console.WriteLine($"Changed {count} methods to public.");

            count = fields.Count(f => !f.IsPublic);
            foreach (var field in fields)
            {
                field.IsPublic = true;
            }

            Console.WriteLine($"Changed {count} fields to public.");
        }

        public static void Exit(int exitCode = 0)
        {
            if (!automaticExit)
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }

            Environment.Exit(exitCode);
        }

        private static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: AssemblyPublicizer.exe [Options]+");
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        public static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition moduleDefinition)
        {
            return moduleDefinition.Types.SelectMany(t => t.NestedTypes.Concat(new TypeDefinition[] { t }));
        }
    }
}