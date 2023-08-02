using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using USSR.Utilities;

namespace USSR
{
    public class Program
    {
        const string VERSION = "1.1.0";
        const string ASSET_CLASS_DB = "classdata.tpk";

        public enum LoadTypes
        {
            Asset,
            Bundle,
            WebData
        }

        static LoadTypes loadType;

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine($"Unity Splash Screen Remover (USSR) v{VERSION} \n");
                Console.WriteLine(
                    "USSR is a CLI tool to remove Unity splash screen logo while keep your logo displayed."
                );
                Console.WriteLine(
                    "USSR didn't directly \"hack\" Unity Editor, instead the generated build. So, not all platforms is supported."
                );
                Console.WriteLine(
                    "For more information, visit USSR repo: https://github.com/kiraio-moe/USSR \n"
                );
                Console.WriteLine("Usage: USSR.exe <path_to_game_executable>");
                Console.WriteLine(
                    "Alternatively, you can just drag and drop your game executable to USSR.exe"
                );
                Console.ReadLine();
                return;
            }

            string? ussrExec = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string? tpkFile = Path.Combine(ussrExec, ASSET_CLASS_DB);

            AssetsManager assetsManager = new();

            try
            {
                Console.WriteLine("Loading class type package...");
                assetsManager.LoadClassPackage(tpkFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Asset class types package not found. {ex.Message}");
                Console.ReadLine();
                return;
            }

            string? execPath = args[0];
            string? execExtension = Path.GetExtension(execPath);
            string? execDirectory = Path.GetDirectoryName(execPath);

            string? dataFile;
            string? dataDirectory; // game data directory

            string? webDataFile = null; // WebGL.[data, data.br, data.gz]
            string[] webDataFileExtensions = { ".data", ".data.br", ".data.gz" };
            string? compressionType = null;
            FileStream? bundleStream = null;

            // List of files to be deleted
            List<string> temporaryFiles = new();

            switch (execExtension)
            {
                case ".exe":
                case ".x86":
                case ".x86_64":
                case ".dmg":
                    dataDirectory = Path.Combine(
                        execDirectory,
                        $"{Path.GetFileNameWithoutExtension(execPath)}_Data"
                    );
                    break;
                case ".html":
                    // Set to the Build folder first
                    dataDirectory = Path.Combine(execDirectory, "Build");

                    // Search for WebGL.* file
                    foreach (string extension in webDataFileExtensions)
                    {
                        webDataFile = Path.Combine(dataDirectory, $"WebGL{extension}");

                        if (File.Exists(webDataFile))
                        {
                            // Set compression type
                            compressionType = extension;
                            break;
                        }
                    }

                    if (!Utility.CheckFile(webDataFile))
                    {
                        Console.ReadLine();
                        return;
                    }

                    Utility.BackupOnlyOnce(webDataFile);

                    string? decompressedWebData = Path.Combine(dataDirectory, "WebGL.data");
                    temporaryFiles.Add(decompressedWebData);
                    loadType = LoadTypes.WebData;

                    switch (compressionType)
                    {
                        case ".data.br":
                            BrotliUtils.DecompressFile(webDataFile, decompressedWebData);
                            break;
                        case ".data.gz":
                            GZipUtils.DecompressFile(webDataFile, decompressedWebData);
                            break;
                    }

                    // Unpack WebGL.data and set the dataDirectory to output folder
                    dataDirectory = UnityWebDataHelper.UnpackBundleToFile(decompressedWebData);

                    break;
                default:
                    Console.WriteLine("Sorry, unsupported platform :(");
                    Console.ReadLine();
                    return;
            }

            Console.WriteLine("Checking for globalgamemanagers...");

            // Default compression
            dataFile = Path.Combine(dataDirectory, "globalgamemanagers");
            loadType = LoadTypes.Asset;

            // LZ4/LZ4HC compression
            if (!File.Exists(dataFile))
            {
                Console.WriteLine(
                    "globalgamemanagers not found. Checking for data.unity3d instead..."
                );

                dataFile = Path.Combine(dataDirectory, "data.unity3d");
                loadType = LoadTypes.Bundle;
            }

            if (!Utility.CheckFile(dataFile))
            {
                Console.ReadLine();
                return;
            }

            // Only backup globalgamemanagers or data.unity3d if not in WebGL
            if (loadType.Equals(LoadTypes.WebData))
                Utility.BackupOnlyOnce(dataFile);

            // Make temporary copy, so the original file ready to be overwritten
            string? tempFile = Utility.CloneFile(dataFile, $"{dataFile}.temp");
            temporaryFiles.Add(tempFile);

            AssetsFileInstance? assetFileInstance = null;
            BundleFileInstance? bundleFileInstance = null;

            try
            {
                switch (loadType)
                {
                    // globalgamemanagers
                    case LoadTypes.Asset:
                        // Load target file and it's dependencies
                        // Loading the dependencies is required to check unity logo asset
                        Console.WriteLine("Loading asset file and it's dependencies...");
                        assetFileInstance = assetsManager.LoadAssetsFile(tempFile, true);

                        break;
                    // data.unity3d
                    case LoadTypes.Bundle:
                        Console.WriteLine("Unpacking asset bundle file...");

                        string? unpackedBundleFile = $"{dataFile}.unpacked";
                        temporaryFiles.Add(unpackedBundleFile);

                        bundleStream = File.Open(unpackedBundleFile, FileMode.Create);

                        bundleFileInstance = assetsManager.LoadBundleFile(tempFile, false);
                        bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                            bundleFileInstance.file,
                            bundleStream
                        );

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

                // bundleStream?.Close();
                assetsManager.UnloadAll(true);
                Utility.CleanUp(temporaryFiles);

                Console.ReadLine();
                return;
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

                switch (loadType)
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
                        /*
                        * IDK why AssetsTools won't load "UnitySplash-cube"
                        * external asset while in Bundle file. So, we can
                        * check it's name and remove it like before.
                        *
                        * Alternatively, we can still find it by checking
                        * the base field. If it's null, then it is.
                        */
                        if (logoExtInfo.baseField == null)
                            unityLogo = data;
                        break;
                }
            }

            /*
            * Remove "UnitySplash-cube" to completely remove
            * Unity splash screen logo. So, Only our logo remained.
            */
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

            try
            {
                // Write modified asset file to disk
                Console.WriteLine("Writing changes to disk...");

                switch (loadType)
                {
                    case LoadTypes.Asset:
                        using (AssetsFileWriter writer = new(dataFile))
                            assetFile?.Write(writer, 0, assetsReplacers);
                        break;
                    case LoadTypes.Bundle:
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

                        string uncompressedBundleFile = $"{dataFile}.uncompressed";
                        temporaryFiles.Add(uncompressedBundleFile);

                        using (AssetsFileWriter writer = new(uncompressedBundleFile))
                            bundleFile?.Write(writer, bundleReplacers);

                        using (
                            FileStream? uncompressedBundleStream = File.OpenRead(
                                uncompressedBundleFile
                            )
                        )
                        {
                            Console.WriteLine("Compressing asset bundle file...");

                            AssetBundleFile? uncompressedBundle = new();
                            uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                            // using AssetsFileReader reader = new(uncompressedBundleStream);
                            using AssetsFileWriter writer = new(dataFile);
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
