using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace kiraio.USSR
{
    public class Program
    {
        const string VERSION = "1.0.0";
        const string ASSET_CLASS_DB = "classdata.tpk";

        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine($"Unity Splash Screen Remover (USSR) v{VERSION} \n");
                Console.WriteLine("USSR is a CLI tool to remove Unity splash screen logo.");
                Console.WriteLine(
                    "USSR didn't directly \"hack\" Unity Editor, but the generated build. So, not all platforms is supported!"
                );
                Console.WriteLine(
                    "For more information, visit USSR repo: https://github.com/kiraio-moe/USSR \n"
                );
                Console.WriteLine("Usage: USSR.exe <game_executable_path>");
                Console.WriteLine(
                    "Alternatively, you can drag and drop game executable to USSR.exe"
                );
                Console.ReadLine();
                return;
            }

            string? ussrPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string? assetClassTypesPackage = Path.Combine(ussrPath, ASSET_CLASS_DB);
            if (!File.Exists(assetClassTypesPackage))
            {
                Console.WriteLine($"Asset class types package not found: {assetClassTypesPackage}");
                Console.WriteLine("The file is moved or deleted.");
                Console.ReadLine();
                return;
            }

            Console.WriteLine("Loading class type package...");
            AssetsManager? assetsManager = new();
            assetsManager.LoadClassPackage(assetClassTypesPackage);

            // Target
            string? execPath = args[0];
            string? execExtension = Path.GetExtension(execPath);
            string? rootPath = Path.GetDirectoryName(execPath);
            string? targetFile;

            switch (execExtension)
            {
                case ".exe":
                case ".x86":
                case ".x86_64":
                case ".dmg":
                    string? dataPath = Path.Combine(
                        rootPath,
                        $"{Path.GetFileNameWithoutExtension(execPath)}_Data"
                    );
                    targetFile = Path.Combine(dataPath, "globalgamemanagers");

                    // Use compression
                    if (!File.Exists(targetFile))
                        targetFile = Path.Combine(dataPath, "data.unity3d");

                    break;
                case ".html":
                    targetFile = Path.Combine(rootPath, "Build", "WebGL.data");
                    // TODO: Process WebGL.data
                    break;
                default:
                    Console.WriteLine("Sorry, unsupported platform.");
                    Console.ReadLine();
                    return;
            }

            // Check game data
            if (!File.Exists(targetFile))
            {
                Console.WriteLine($"{targetFile} doesn't exists!");
                Console.ReadLine();
                return;
            }

            // Make temporary copy
            string? inspectedFile = CloneFile(
                targetFile,
                backupDestinationPath: $"{targetFile}.temp"
            );

            Console.WriteLine("Loading asset file and it's dependencies...");
            // Load target file and it's dependencies
            // Loading the dependencies is required to check unity logo asset
            AssetsFileInstance? assetFileInstance = assetsManager.LoadAssetsFile(
                inspectedFile,
                true
            );

            AssetsFile assetFile = assetFileInstance.file;

            Console.WriteLine("Loading asset class types database...");
            assetsManager.LoadClassDatabaseFromPackage(assetFile.Metadata.UnityVersion);

            List<AssetFileInfo>? buildSettingsInfo = assetFile.GetAssetsOfType(
                AssetClassID.BuildSettings
            );
            // Get base field
            AssetTypeValueField? buildSettingsBase = assetsManager.GetBaseField(
                assetFileInstance,
                buildSettingsInfo[0]
            );

            List<AssetFileInfo>? playerSettingsInfo = assetFile.GetAssetsOfType(
                AssetClassID.PlayerSettings
            );
            // Get base field
            AssetTypeValueField? playerSettingsBase = assetsManager.GetBaseField(
                assetFileInstance,
                playerSettingsInfo[0]
            );
            AssetTypeValueField? splashScreenLogos = playerSettingsBase[
                "m_SplashScreenLogos.Array"
            ];

            // Get necessary fields
            bool isProVersion = buildSettingsBase["hasPROVersion"].AsBool;
            bool showUnityLogo = playerSettingsBase["m_ShowUnitySplashLogo"].AsBool;

            if (isProVersion && !showUnityLogo)
            {
                Console.WriteLine(
                    "Unity splash screen didn't exist or already removed. Nothing to do."
                );
                assetsManager.UnloadAssetsFile(inspectedFile);
                File.Delete(inspectedFile);
                Console.ReadLine();
                return;
            }

            // Backup target file
            Console.WriteLine("Backup original file...");
            CloneFile(targetFile, backupDestinationPath: $"{targetFile}.bak");

            Console.WriteLine("Removing Unity splash screen...");

            // Remove Unity splash screen by flipping these boolean fields
            buildSettingsBase["hasPROVersion"].AsBool = !isProVersion; // true
            playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false

            // Iterate over "m_SplashScreenLogos" to find Unity splash screen logo
            AssetTypeValueField? unityLogo = null;

            foreach (AssetTypeValueField data in splashScreenLogos)
            {
                AssetTypeValueField? logoPointer = data["logo"];
                AssetExternal logoExtInfo = assetsManager.GetExtAsset(
                    assetFileInstance,
                    logoPointer
                );
                AssetTypeValueField? logoBase = logoExtInfo.baseField;
                string? logoName = logoBase["m_Name"].AsString;

                // If it's Unity splash screen logo
                if (logoName.Contains("UnitySplash-cube"))
                {
                    unityLogo = data;
                    break;
                }
            }

            // Remove Unity splash screen logo to make sure we completely remove Unity splash screen. Only our logo remained.
            splashScreenLogos.Children.Remove(unityLogo);

            Console.WriteLine(
                $"hasPROVersion: {buildSettingsBase["hasPROVersion"].AsBool} | m_ShowUnitySplashLogo: {playerSettingsBase["m_ShowUnitySplashLogo"].AsBool} | UnitySplash-cube: {splashScreenLogos.Children.Contains(unityLogo)}"
            );

            // Store modified base fields
            List<AssetsReplacer>? replacers =
                new()
                {
                    new AssetsReplacerFromMemory(
                        assetFile,
                        buildSettingsInfo[0],
                        buildSettingsBase
                    ),
                    new AssetsReplacerFromMemory(
                        assetFile,
                        playerSettingsInfo[0],
                        playerSettingsBase
                    )
                };

            try
            {
                // Write modified asset file to disk
                using AssetsFileWriter writer = new(targetFile);
                Console.WriteLine("Writing changes to disk...");
                assetFile.Write(writer, 0, replacers);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed to save file! {ex.Message} Make sure to close any processes that use it."
                );
                Console.ReadLine();
                return;
            }

            Console.WriteLine("Successfully removed Unity splash screen. Enjoy :) \n");
            Console.WriteLine(
                "Don't forget to visit USSR repo: https://github.com/kiraio-moe/USSR and give it a star!"
            );
            Console.ReadLine();
        }

        /// <summary>
        /// Clone a file.
        /// </summary>
        /// <param name="sourceFilePath"></param>
        /// <param name="backupDestinationPath"></param>
        /// <returns>Cloned file path</returns>
        static string CloneFile(string sourceFilePath, string backupDestinationPath)
        {
            try
            {
                // Check if the source file exists
                if (!File.Exists(sourceFilePath))
                {
                    return new FileNotFoundException(
                        "Backup source file does not exist!"
                    ).ToString();
                }

                // Create the backup destination directory if it doesn't exist
                string? backupDir = Path.GetDirectoryName(backupDestinationPath);
                if (Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                // Copy the source file to the backup destination
                File.Copy(sourceFilePath, backupDestinationPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during the backup process: {ex.Message}");
            }

            return backupDestinationPath;
        }
    }
}
