using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Kiraio.USSR
{
    public class Program
    {
        const string VERSION = "1.0.0";
        const string ASSET_CLASS_DB = "classdata.tpk";

        public enum LoadTypes
        {
            Asset,
            Bundle
        }

        static LoadTypes loadTypes;

        static void Main(string[] args)
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

            string? execPath = args[0];
            string? execExtension = Path.GetExtension(execPath);
            string? rootPath = Path.GetDirectoryName(execPath);
            string? targetFile;
            List<string> temporaryFiles = new();

            Console.WriteLine("Checking for supported platforms...");
            switch (execExtension)
            {
                case ".exe":
                case ".x86":
                case ".x86_64":
                case ".dmg":
                    Console.WriteLine("Supported.");

                    string? dataPath = Path.Combine(
                        rootPath,
                        $"{Path.GetFileNameWithoutExtension(execPath)}_Data"
                    );

                    Console.WriteLine("Checking for globalgamemanagers...");

                    // Default compression
                    targetFile = Path.Combine(dataPath, "globalgamemanagers");
                    loadTypes = LoadTypes.Asset;

                    // LZMA/LZ4 compression
                    if (!File.Exists(targetFile))
                    {
                        Console.WriteLine(
                            "globalgamemanagers not found. Checking for data.unity3d instead..."
                        );

                        targetFile = Path.Combine(dataPath, "data.unity3d");
                        loadTypes = LoadTypes.Bundle;
                    }

                    break;
                case ".html":
                    // TODO: Process WebGL.data
                    // targetFile = Path.Combine(rootPath, "Build", "WebGL.data");
                    // break;
                    return;
                default:
                    Console.WriteLine("Sorry, unsupported platform :(");
                    Console.ReadLine();
                    return;
            }

            // Check game data
            if (!File.Exists(targetFile))
            {
                Console.WriteLine($"{targetFile} doesn't exists!");
                Console.WriteLine("The file is moved or deleted.");
                Console.ReadLine();
                return;
            }

            // Make temporary copy
            string? inspectedFile = Utility.CloneFile(targetFile, $"{targetFile}.temp");
            temporaryFiles.Add(inspectedFile);

            AssetsFileInstance? assetFileInstance = null;
            BundleFileInstance? bundleFileInstance = null;
            FileStream? bundleStream = null;

            try
            {
                switch (loadTypes)
                {
                    // globalgamemanagers
                    case LoadTypes.Asset:
                        // Load target file and it's dependencies
                        // Loading the dependencies is required to check unity logo asset
                        Console.WriteLine("Loading asset file and it's dependencies...");
                        assetFileInstance = assetsManager.LoadAssetsFile(inspectedFile, true);
                        break;
                    // data.unity3d
                    case LoadTypes.Bundle:
                        Console.WriteLine("Loading asset bundle file...");
                        bundleFileInstance = assetsManager.LoadBundleFile(inspectedFile, false);

                        string? bundleStreamFile = $"{targetFile}.stream";
                        bundleStream = File.Open(bundleStreamFile, FileMode.Create);

                        bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                            bundleFileInstance.file,
                            bundleStream
                        );

                        // Add to cleanup chores
                        temporaryFiles.Add(bundleStreamFile);

                        Console.WriteLine("Loading asset file and it's dependencies...");
                        assetFileInstance = assetsManager.LoadAssetsFileFromBundle(
                            bundleFileInstance,
                            0,
                            true
                        );

                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading asset file. {ex.Message}");
                Console.ReadLine();
                return;
            }

            AssetBundleFile? bundleFile = bundleFileInstance?.file;
            AssetsFile? assetFile = assetFileInstance?.file;

            Console.WriteLine("Loading asset class types database...");
            assetsManager.LoadClassDatabaseFromPackage(assetFile?.Metadata.UnityVersion);

            List<AssetFileInfo>? buildSettingsInfo = assetFile?.GetAssetsOfType(
                AssetClassID.BuildSettings
            );
            // Get BuildSettings base field
            AssetTypeValueField? buildSettingsBase = assetsManager.GetBaseField(
                assetFileInstance,
                buildSettingsInfo?[0]
            );

            List<AssetFileInfo>? playerSettingsInfo = assetFile?.GetAssetsOfType(
                AssetClassID.PlayerSettings
            );
            // Get PlayerSettings base field
            AssetTypeValueField? playerSettingsBase = assetsManager.GetBaseField(
                assetFileInstance,
                playerSettingsInfo?[0]
            );
            // Get m_SplashScreenLogos field as array
            AssetTypeValueField? splashScreenLogos = playerSettingsBase[
                "m_SplashScreenLogos.Array"
            ];

            // Get required fields to remove the splash screen
            bool isProVersion = buildSettingsBase["hasPROVersion"].AsBool;
            bool showUnityLogo = playerSettingsBase["m_ShowUnitySplashLogo"].AsBool;

            if (isProVersion && !showUnityLogo)
            {
                Console.WriteLine(
                    "Unity splash screen logo didn't exist or already removed. Nothing to do."
                );

                bundleStream?.Close();
                assetsManager.UnloadAll(true);
                Utility.CleanUp(temporaryFiles);

                Console.ReadLine();
                return;
            }

            // Backup original file
            string? backupOriginalFile = $"{targetFile}.bak";
            if (!File.Exists(backupOriginalFile))
            {
                Console.WriteLine("Backup original file...");
                Utility.CloneFile(targetFile, backupOriginalFile);
            }

            Console.WriteLine("Removing Unity splash screen...");

            // Remove Unity splash screen by flipping these boolean fields
            buildSettingsBase["hasPROVersion"].AsBool = !isProVersion; // true
            playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false

            // Iterate over "m_SplashScreenLogos" to find Unity splash screen logo
            AssetTypeValueField? unityLogo = null;

            foreach (AssetTypeValueField data in splashScreenLogos)
            {
                // Get the Sprite asset
                AssetTypeValueField? logoPointer = data?["logo"];
                // Get the external asset
                AssetExternal logoExtInfo = assetsManager.GetExtAsset(
                    assetFileInstance,
                    logoPointer
                );

                // IDK why AssetsTools won't load "UnitySplash-cube"
                // external asset while in Bundle file. So, we can
                // check it's name then remove it.
                // So, we break it into 2 types of file load.
                switch (loadTypes)
                {
                    case LoadTypes.Asset:
                        // Get the base field
                        AssetTypeValueField? logoBase = logoExtInfo.baseField;
                        string? logoName = logoBase["m_Name"].AsString;

                        // If it's Unity splash screen logo
                        if (logoName.Contains("UnitySplash-cube"))
                            unityLogo = data;

                        break;
                    case LoadTypes.Bundle:
                        // After some testing, I realize only Unity
                        // splash screen logo external asset that
                        // won't load. So, we can use it to mark
                        // that this is the Unity splash screen logo
                        if (logoExtInfo.baseField == null)
                            unityLogo = data;
                        break;
                }
            }

            // Remove Unity splash screen logo to completely remove
            // Unity splash screen logo. Only our logo remained.
            if (unityLogo != null)
                splashScreenLogos?.Children.Remove(unityLogo);

            Console.WriteLine("Done.");

            // Store modified base fields
            List<AssetsReplacer>? assetsReplacers =
                new()
                {
                    new AssetsReplacerFromMemory(
                        assetFile,
                        buildSettingsInfo?[0],
                        buildSettingsBase
                    ),
                    new AssetsReplacerFromMemory(
                        assetFile,
                        playerSettingsInfo?[0],
                        playerSettingsBase
                    )
                };

            List<BundleReplacer> bundleReplacers =
                new()
                {
                    new BundleReplacerFromAssets(
                        assetFileInstance?.name,
                        null,
                        assetFile,
                        assetsReplacers
                    )
                };

            FileStream? uncompressedBundleStream = null;

            try
            {
                // Write modified asset file to disk
                Console.WriteLine("Writing changes to disk...");

                switch (loadTypes)
                {
                    case LoadTypes.Asset:
                        using (AssetsFileWriter writer = new(targetFile))
                        {
                            assetFile?.Write(writer, 0, assetsReplacers);
                        }
                        break;
                    case LoadTypes.Bundle:
                        string uncompressedBundleFile = $"{targetFile}.uncompressed";
                        temporaryFiles.Add(uncompressedBundleFile);

                        using (AssetsFileWriter writer = new(uncompressedBundleFile))
                        {
                            bundleFile?.Write(writer, bundleReplacers);
                        }

                        uncompressedBundleStream = File.OpenRead(uncompressedBundleFile);

                        AssetBundleFile? uncompressedBundle = new();
                        uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                        using (AssetsFileReader reader = new(uncompressedBundleStream))
                        {
                            Console.WriteLine("Compressing asset bundle file...");

                            using AssetsFileWriter writer = new(targetFile);
                            uncompressedBundle.Pack(
                                uncompressedBundle.Reader,
                                writer,
                                AssetBundleCompressionType.LZ4
                            );
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed to save file! {ex.Message} Make sure to close any processes that use it."
                );
                Console.ReadLine();
                return;
            }

            // Cleanup temporary files
            Console.WriteLine("Cleaning up temporary files...");

            bundleStream?.Close();
            uncompressedBundleStream?.Close();
            assetsManager.UnloadAllBundleFiles();
            assetsManager.UnloadAllAssetsFiles(true);
            Utility.CleanUp(temporaryFiles);

            Console.WriteLine("Successfully removed Unity splash screen. Enjoy :) \n");
            Console.WriteLine(
                "Don't forget to visit USSR repo: https://github.com/kiraio-moe/USSR and give it a star!"
            );
            Console.ReadLine();
        }
    }
}
