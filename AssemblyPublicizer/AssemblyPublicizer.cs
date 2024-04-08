using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
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
        private const string BaseUrl = "https://thunderstore.io";
        private const string ApiPath = "/c/valheim/api/v1/package";
        private const string BepInExPackageName = "denikson/BepInExPack_Valheim/";
        private const string BepInExPackageNameWVersion = "denikson-BepInExPack_Valheim";
        private static bool _automaticExit, _help;

        static async Task Main(string[] args)
        {
            string suffix = "_publicized";
            string defaultOutputDir = "publicized_assemblies";

            List<string> inputs = new List<string>();
            string outputDir = "";

            OptionSet options = new OptionSet
            {
                { "i|input=", "Paths (relative or absolute) to the input assemblies, separated by comma", i => inputs = i.Split(',').ToList() },
                { "o|output=", "[Optional] Output directory for the modified assemblies", o => outputDir = o },
                { "e|exit", "Application should automatically exit", e => _automaticExit = e != null },
                { "h|help", "Show this message", h => _help = h != null }
            };

            try
            {
                List<string> extra = options.Parse(args);
                if (_help)
                    ShowHelp(options);

                if (!inputs.Any() && extra.Count >= 1)
                {
                    inputs = extra.Take(extra.Count - 1).ToList();
                    outputDir = extra.LastOrDefault();
                }

                if (!inputs.Any())
                {
                    string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    inputs = Directory.GetFiles(currentDirectory, "*.dll").ToList();
                }

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

            foreach (string inputFile in inputs)
            {
                ProcessAssembly(inputFile, outputDir, suffix);
            }

            // After processing assemblies, download and install BepInEx
            try
            {
                await DownloadAndInstallBepInEx();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
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
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR! Cannot read the assembly. Please check your permissions. Exception: {ex.Message}");
                return;
            }

            IEnumerable<TypeDefinition> allTypes = GetAllTypes(assembly.MainModule);
            IEnumerable<MethodDefinition> allMethods = allTypes.SelectMany(t => t.Methods);
            IEnumerable<FieldDefinition> allFields = allTypes.SelectMany(t => t.Fields);

            MakePublic(allTypes, allMethods, allFields);

            string outputName = $"{Path.GetFileNameWithoutExtension(inputFile)}{suffix}{Path.GetExtension(inputFile)}";
            string outputFile = Path.Combine(outputDir, outputName);
            try
            {
                assembly.Write(outputFile);
                Console.WriteLine($"Saved: {outputFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR! Cannot create/overwrite the new assembly. Exception: {ex.Message}");
            }
        }

        static void MakePublic(IEnumerable<TypeDefinition> types, IEnumerable<MethodDefinition> methods, IEnumerable<FieldDefinition> fields)
        {
            Console.WriteLine($"Making types, methods, and fields public...");

            int typeCount = types.Count(t => !t.IsPublic && !t.IsNestedPublic);
            foreach (TypeDefinition? type in types)
            {
                if (!type.IsPublic)
                {
                    type.IsPublic = true;
                    //Console.WriteLine($"Type made public: {type.FullName}");
                }

                if (type.IsNested && !type.IsNestedPublic)
                {
                    type.IsNestedPublic = true;
                    //Console.WriteLine($"Nested type made public: {type.FullName}");
                }
            }

            Console.WriteLine($"Changed {typeCount} types to public.");

            int methodCount = methods.Count(m => !m.IsPublic);
            foreach (MethodDefinition? method in methods)
            {
                if (!method.IsPublic)
                {
                    method.IsPublic = true;
                    //Console.WriteLine($"Method made public: {method.FullName}");
                }
            }

            Console.WriteLine($"Changed {methodCount} methods to public.");

            int fieldCount = fields.Count(f => !f.IsPublic);
            foreach (FieldDefinition? field in fields)
            {
                if (!field.IsPublic)
                {
                    field.IsPublic = true;
                    //Console.WriteLine($"Field made public: {field.FullName}");
                }
            }

            //Console.WriteLine($"Changed {fieldCount} fields to public.");
        }

        private static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: AssemblyPublicizer.exe [Options]+");
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        public static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition moduleDefinition)
        {
            return _GetAllNestedTypes(moduleDefinition.Types);
        }

        private static IEnumerable<TypeDefinition> _GetAllNestedTypes(IEnumerable<TypeDefinition> typeDefinitions)
        {
            if (typeDefinitions?.Any() != true)
                return new List<TypeDefinition>();

            IEnumerable<TypeDefinition> result = typeDefinitions.Concat(_GetAllNestedTypes(typeDefinitions.SelectMany(t => t.NestedTypes)));

            return result;
        }

        private static void Exit(int exitCode = 0)
        {
            if (!_automaticExit)
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }

            Environment.Exit(exitCode);
        }

        private static async Task DownloadAndInstallBepInEx()
        {
            HttpClient httpClient = new HttpClient();
            string latestVersion = await GetLatestVersionAsync(httpClient);
            if (latestVersion == null)
            {
                Console.WriteLine("Failed to get the latest version of BepInEx.");
                return;
            }

            string downloadUrl = $"{BaseUrl}/package/download/{BepInExPackageName}{latestVersion}/";
            string zipFilePath = await DownloadFileAsync(httpClient, downloadUrl);
            if (zipFilePath == null)
            {
                Console.WriteLine("Failed to download the BepInEx package.");
                return;
            }

            string gameFolderPath = GetGameFolderPath();
            ExtractAndInstall(zipFilePath, gameFolderPath);
            Console.WriteLine("BepInEx has been successfully installed.");
            // Publicize the BepInEx assemblies found in BepInEx/core folder and add them to the publicized_assemblies folder
            string bepInExCorePath = Path.Combine(gameFolderPath, "BepInEx", "core");
            if (Directory.Exists(bepInExCorePath))
            {
                string publicizedAssembliesPath = Path.Combine(gameFolderPath, "BepInEx", "core", "publicized_assemblies");
                if (!Directory.Exists(publicizedAssembliesPath))
                {
                    Directory.CreateDirectory(publicizedAssembliesPath);
                }

                foreach (string file in Directory.GetFiles(bepInExCorePath, "*.dll"))
                {
                    ProcessAssembly(file, publicizedAssembliesPath, "_publicized");
                }
            }
        }

        private static async Task<string> GetLatestVersionAsync(HttpClient httpClient)
        {
            try
            {
                string requestUrl = $"{BaseUrl}{ApiPath}/";
                string? response = await httpClient.GetStringAsync(requestUrl);
                List<PackageListing>? packages = JsonSerializer.Deserialize<List<PackageListing>>(response);

                if (packages != null)
                {
                    // List all packages
                    foreach (PackageListing? package in packages)
                    {
                        if (package.full_name.Contains("denikson"))
                        {
                            Console.WriteLine($"{package.full_name} - {package.versions[0].version_number}");
                        }
                    }

                    PackageListing? packageFirst = packages.FirstOrDefault(p => p.full_name.StartsWith(BepInExPackageNameWVersion, StringComparison.Ordinal));
                    if (packageFirst != null)
                    {
                        return packageFirst.versions[0].version_number;
                    }
                    else
                    {
                        Console.WriteLine($"Package {BepInExPackageName} not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching package list from {BaseUrl}{ApiPath}: {ex.Message}");
            }

            return null;
        }

        private static async Task<string> DownloadFileAsync(HttpClient httpClient, string downloadUrl)
        {
            try
            {
                // Add an Accept-Encoding header to request compressed content
                httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));

                HttpResponseMessage? response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Failed to download the file. Status code: " + response.StatusCode);
                    return null;
                }

                string tempFilePath = Path.GetTempFileName() + ".zip"; // Add .zip extension to the temp file
                using (FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                return tempFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error downloading file: " + ex.Message);
            }

            return null;
        }

        private static void ExtractAndInstall(string zipFilePath, string destinationFolderPath)
        {
            try
            {
                // Ensure the destination directory exists
                if (!Directory.Exists(destinationFolderPath))
                {
                    Directory.CreateDirectory(destinationFolderPath);
                }

                Console.WriteLine("Extracting BepInEx to " + destinationFolderPath);
                // Extract the zip file content to the destination directory and overwrite existing files
                ZipFile.ExtractToDirectory(zipFilePath, destinationFolderPath);

                Console.WriteLine($"Extracted ZIP file to {destinationFolderPath}");

                // Delete the zip file after extraction if it's no longer needed
                File.Delete(zipFilePath);

                // Copy all files from BepInExPack_Valheim to the game folder which is the destinationFolderPath
                string bepInExPackValheimPath = Path.Combine(destinationFolderPath, "BepInExPack_Valheim");
                if (Directory.Exists(bepInExPackValheimPath))
                {
                    foreach (string file in Directory.GetFiles(bepInExPackValheimPath, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = file.Substring(bepInExPackValheimPath.Length + 1);
                        string destinationPath = Path.Combine(destinationFolderPath, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destination path is invalid."));
                        File.Copy(file, destinationPath, true);
                    }
                }

                // Delete the BepInExPack_Valheim folder after copying all files
                Directory.Delete(bepInExPackValheimPath, true);

                // Delete the icon.png, CHANGELOG.md, manifest.json and README.md files from the destination folder
                File.Delete(Path.Combine(destinationFolderPath, "icon.png"));
                File.Delete(Path.Combine(destinationFolderPath, "CHANGELOG.md"));
                File.Delete(Path.Combine(destinationFolderPath, "manifest.json"));
                File.Delete(Path.Combine(destinationFolderPath, "README.md"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting ZIP file: {ex.Message}");
            }
        }


        private static string GetGameFolderPath()
        {
            DirectoryInfo managedDirectory = new DirectoryInfo(Environment.CurrentDirectory);
            string gameFolderPath = managedDirectory.Parent?.Parent?.FullName; // Navigate up to the game folder from Managed
            Console.WriteLine("Game folder path: " + gameFolderPath);
            return gameFolderPath ?? throw new InvalidOperationException("Game folder path could not be determined.");
        }

        public class VersionInfo
        {
            public string? name { get; set; }
            public string? full_name { get; set; }
            public string? description { get; set; }
            public string? icon { get; set; }
            public string? version_number { get; set; }
            public List<string>? dependencies { get; set; }
            public string? download_url { get; set; }
            public int downloads { get; set; }
            public string? date_created { get; set; }
            public string? website_url { get; set; }
            public bool is_active { get; set; }
            public string? uuid4 { get; set; }
            public int file_size { get; set; }
        }

        public class PackageListing
        {
            public string? name { get; set; }
            public string? full_name { get; set; } = string.Empty;
            public string? owner { get; set; }
            public string? package_url { get; set; }
            public string? donation_link { get; set; }
            public string? date_created { get; set; }
            public string? date_updated { get; set; }
            public string? uuid4 { get; set; }
            public int rating_score { get; set; }
            public bool is_pinned { get; set; }
            public bool is_deprecated { get; set; }
            public bool has_nsfw_content { get; set; }
            public List<string>? categories { get; set; }
            public List<VersionInfo>? versions { get; set; }
        }
    }
}