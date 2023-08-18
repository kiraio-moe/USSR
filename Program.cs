using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Spectre.Console;
using USSR.Utilities;

namespace USSR
{
    public class Program
    {
        const string VERSION = "1.1.1";
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
            AnsiConsole.Background = Color.Grey11;

            if (args.Length < 1)
            {
                AnsiConsole.MarkupLineInterpolated($"[bold red]Unity Splash Screen Remover v{VERSION}[/]");
                Console.WriteLine();
                AnsiConsole.MarkupLine("USSR is a CLI tool to remove Unity splash screen logo while keep your logo displayed.");
                AnsiConsole.MarkupLine("USSR didn't directly \"hack\" Unity Editor, instead the generated build. So, not all platforms is supported.");
                AnsiConsole.MarkupLine("For more information, visit USSR GitHub repo: [link]https://github.com/kiraio-moe/USSR[/]");
                Console.WriteLine();
                AnsiConsole.MarkupLine("[bold green]Usage:[/]");
                AnsiConsole.MarkupLine("[yellow]USSR.exe <path/to/game/executable>[/]");
                AnsiConsole.MarkupLine("Alternatively, you can just drag and drop your game executable to USSR.exe");
                Console.ReadLine();
                return;
            }

            string? ussrExec = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string? tpkFile = Path.Combine(ussrExec, ASSET_CLASS_DB);

            AssetsManager assetsManager = new();

            try
            {
                AnsiConsole.MarkupLine("Loading class type package...");
                assetsManager.LoadClassPackage(tpkFile);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Asset class types package not found![/] {ex.Message}");
                Console.ReadLine();
                return;
            }

            string? execPath = args[0];
            string? execExtension = Path.GetExtension(execPath);
            string? execDirectory = Path.GetDirectoryName(execPath);

            string? ggmFile;
            string? dataDirectory; // Can be "Game_Data" or "Build" directory

            string? webDataFile = null; // WebGL.[data, data.br, data.gz]
            string[] webDataFileExtensions = { ".data", ".data.br", ".data.gz" };
            string? unpackedWebDataDirectory = null;
            string? compressionType = null;

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

                    // Decompress WebGL.data.* first
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
                    unpackedWebDataDirectory = UnityWebDataHelper.UnpackWebDataToFile(
                        decompressedWebData
                    );
                    dataDirectory = unpackedWebDataDirectory;

                    break;
                default:
                    AnsiConsole.MarkupLine("[red]Sorry, unsupported platform :([/]");
                    Console.ReadLine();
                    return;
            }

            AnsiConsole.MarkupLine("Checking for globalgamemanagers...");

            // Default compression
            ggmFile = Path.Combine(dataDirectory, "globalgamemanagers");
            loadType = LoadTypes.Asset;

            // LZ4/LZ4HC compression
            if (!File.Exists(ggmFile))
            {
                AnsiConsole.MarkupLine(
                    "[red]globalgamemanagers not found.[/] Checking for data.unity3d instead..."
                );

                ggmFile = Path.Combine(dataDirectory, "data.unity3d");
                loadType = LoadTypes.Bundle;
            }

            if (!Utility.CheckFile(ggmFile))
            {
                Console.ReadLine();
                return;
            }

            // Only backup globalgamemanagers or data.unity3d if not in WebGL
            if (!(loadType == LoadTypes.WebData))
                Utility.BackupOnlyOnce(ggmFile);

            // Make temporary copy, so the original file ready to be overwritten
            string? tempFile = Utility.CloneFile(ggmFile, $"{ggmFile}.temp");
            temporaryFiles.Add(tempFile);

            AssetsFileInstance? assetFileInstance = null;
            BundleFileInstance? bundleFileInstance = null;
            FileStream? bundleStream = null;

            try
            {
                switch (loadType)
                {
                    // globalgamemanagers
                    case LoadTypes.Asset:
                        // Load target file and it's dependencies
                        // Loading the dependencies is required to check unity logo asset
                        AnsiConsole.MarkupLine("Loading asset file and it's dependencies...");
                        assetFileInstance = assetsManager.LoadAssetsFile(tempFile, true);

                        break;
                    // data.unity3d
                    case LoadTypes.Bundle:
                        AnsiConsole.MarkupLine("Unpacking asset bundle file...");

                        string? unpackedBundleFile = $"{ggmFile}.unpacked";
                        temporaryFiles.Add(unpackedBundleFile);

                        bundleStream = File.Open(unpackedBundleFile, FileMode.Create);

                        bundleFileInstance = assetsManager.LoadBundleFile(tempFile, false);
                        bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                            bundleFileInstance.file,
                            bundleStream
                        );

                        AnsiConsole.MarkupLine("Loading asset file and it's dependencies...");
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
                AnsiConsole.MarkupLine($"Error loading asset file. {ex.Message}");
                Console.ReadLine();
                return;
            }

            AssetBundleFile? bundleFile = bundleFileInstance?.file;
            AssetsFile? assetFile = assetFileInstance?.file;

            AnsiConsole.MarkupLine("Loading asset class types database...");
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
                AnsiConsole.MarkupLine(
                    "Unity splash screen logo didn't exist or already removed. Nothing to do."
                );

                assetsManager.UnloadAll(true);
                Utility.CleanUp(temporaryFiles);

                if (loadType == LoadTypes.WebData)
                    Directory.Delete(dataDirectory, true);

                Console.ReadLine();
                return;
            }

            AnsiConsole.MarkupLine("Sometimes USSR can\'t automatically detect Unity splash screen logo and it\'s leading to accidentally removing your own logo. So, USSR need more information to tackle this such as the logo duration.");
            AnsiConsole.MarkupLine("[red]Please make difference with the logo duration when you build your game! If your logo and Unity logo have same duration, USSR will remove both of them.[/] If no value provided, USSR will use it\'s own way to detect it and may removing your own logo.");
            AnsiConsole.Markup("[green](Optional)[/] Please enter Unity splash screen logo duration: ");

            int.TryParse(Console.ReadLine(), System.Globalization.NumberStyles.Integer, null, out int logoDuration);

            AnsiConsole.MarkupLine("Removing Unity splash screen...");

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

                if (logoExtInfo.baseField != null)
                {
                    // Get the base field
                    AssetTypeValueField? logoBase = logoExtInfo.baseField;
                    string? logoName = logoBase["m_Name"].AsString;

                    // If it's Unity splash screen logo
                    if (logoName.Contains("UnitySplash-cube"))
                        unityLogo = data;
                }
                else
                {
                    /*
                    * IDK why AssetsTools won't load "UnitySplash-cube"
                    * external asset while in Bundle file. So, we can
                    * check it's name and remove it like before.
                    *
                    * Alternatively, we can still find it by using
                    * logo duration or checking if the base field is null.
                    */
                    if (data?["duration"].AsInt == logoDuration)
                        unityLogo = data;
                    else
                        unityLogo = data;
                }
            }

            /*
            * Remove "UnitySplash-cube" to completely remove
            * Unity splash screen logo. So, Only our logo remained.
            */
            if (unityLogo != null)
                splashScreenLogos?.Children.Remove(unityLogo);

            AnsiConsole.Markup("Done.\n");

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
                AnsiConsole.MarkupLine("Writing changes to disk...");

                switch (loadType)
                {
                    case LoadTypes.Asset:
                        using (AssetsFileWriter writer = new(ggmFile))
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

                        string uncompressedBundleFile = $"{ggmFile}.uncompressed";
                        temporaryFiles.Add(uncompressedBundleFile);

                        using (AssetsFileWriter writer = new(uncompressedBundleFile))
                            bundleFile?.Write(writer, bundleReplacers);

                        using (
                            FileStream? uncompressedBundleStream = File.OpenRead(
                                uncompressedBundleFile
                            )
                        )
                        {
                            AnsiConsole.MarkupLine("Compressing asset bundle file...");

                            AssetBundleFile? uncompressedBundle = new();
                            uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                            using AssetsFileWriter writer = new(ggmFile);
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
                AnsiConsole.MarkupLine(
                    $"[red]Failed to save file![/] {ex.Message} Make sure to close any processes that use it."
                );
                Console.ReadLine();
                return;
            }

            // Cleanup temporary files
            AnsiConsole.MarkupLine("Cleaning up temporary files...");
            bundleStream?.Close();
            assetsManager.UnloadAllBundleFiles();
            assetsManager.UnloadAllAssetsFiles(true);
            Utility.CleanUp(temporaryFiles);

            if (loadType == LoadTypes.WebData)
            {
                AnsiConsole.MarkupLine("Packing WebGL...");

                string? webGLdataPath = Path.Combine(execDirectory, "Build", "WebGL.data");

                UnityWebDataHelper.PackFilesToWebData(
                    unpackedWebDataDirectory,
                webGLdataPath
                );

                // Delete WebGL folder
                Directory.Delete(unpackedWebDataDirectory, true);

                // Compress WebGL.data if using compression
                AnsiConsole.MarkupLine("Compressing WebGL.data...");

                switch (compressionType)
                {
                    case ".data.br":
                        BrotliUtils.CompressFile(webGLdataPath, $"{webGLdataPath}.br");
                        break;
                    case ".data.gz":
                        GZipUtils.CompressFile(webGLdataPath, $"{webGLdataPath}.gz");
                        break;
                }

                // Delete WebGL.data
                File.Delete(webGLdataPath);
            }

            AnsiConsole.MarkupLine("[green]Successfully remove Unity splash screen.[/] Enjoy :) \n");
            AnsiConsole.MarkupLine(
                "Don't forget to visit USSR repo: https://github.com/kiraio-moe/USSR and give it a star!"
            );
            Console.ReadLine();
        }
    }
}
