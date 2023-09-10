using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Spectre.Console;
using USSR.Utilities;

namespace USSR
{
    public class Program
    {
        const string VERSION = "1.1.3";
        const string ASSET_CLASS_DB = "classdata.tpk";
        static readonly byte[] globalgamemanagersMagic = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x16, 0x00, 0x00, 0x00, 0x00 };
        static readonly byte[] unity3dMagic = { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00, 0x00, 0x00, 0x00, 0x08 };

        static void Main(string[] args)
        {
            AnsiConsole.Background = Color.Grey11;

            if (args.Length < 1)
            {
                PrintHelp();
                Console.ReadLine();
                return;
            }

            AnsiConsole.MarkupLineInterpolated(
                $"[bold red]Unity Splash Screen Remover v{VERSION}[/]"
            );

            string? ussrExec = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string? tpkFile = Path.Combine(ussrExec, ASSET_CLASS_DB);

            AssetsManager assetsManager = new();
            LoadClassPackage(assetsManager, tpkFile);

            string? execFile = args[0];
            string? execExtension = Path.GetExtension(execFile);
            string? execDirectory = Path.GetDirectoryName(execFile);

            string? gameDataDirectory,
                globalgamemanagersFile, unity3dFile;

            bool isWebGL = false;
            string? webGLFile = null, rawWebGLFile = null; // WebGL.(data|data.br|data.gz)
            string[] webGLFileExtensions = { ".data", ".data.br", ".data.gz" };
            string? unpackedWebGLDirectory = null;
            string? webGLCompressionType = null;

            // List of files to be deleted later
            List<string> temporaryFiles = new();

            if (Utility.IsFile(execFile, globalgamemanagersMagic) || Utility.IsFile(execFile, unity3dMagic))
            {
                gameDataDirectory = Path.GetDirectoryName(execFile);
            }
            else
            {
                switch (execExtension)
                {
                    case ".exe":
                    case ".x86":
                    case ".x86_64":
                    case ".dmg":
                        gameDataDirectory = Path.Combine(
                            execDirectory,
                            $"{Path.GetFileNameWithoutExtension(execFile)}_Data"
                        );
                        break;
                    case ".html":
                        isWebGL = true;
                        gameDataDirectory = Path.Combine(execDirectory, "Build");
                        rawWebGLFile = Path.Combine(gameDataDirectory, "WebGL.data");

                        // Search for WebGL.* file
                        foreach (string extension in webGLFileExtensions)
                        {
                            if (File.Exists(webGLFile = Path.Combine(gameDataDirectory, $"WebGL{extension}")))
                            {
                                webGLCompressionType = extension;
                                break;
                            }
                        }

                        if (!File.Exists(webGLFile))
                        {
                            AnsiConsole.MarkupLine("[red]No any WebGL.data found.[/]");
                            Console.ReadLine();
                            return;
                        }

                        AnsiConsole.MarkupLineInterpolated($"Found [green]{Path.GetFileName(webGLFile)}[/].");

                        // Backup WebGL.* file
                        Utility.BackupOnlyOnce(webGLFile);

                        switch (webGLCompressionType)
                        {
                            case ".data.br":
                                AnsiConsole.MarkupLineInterpolated($"Decompress [green]{Path.GetFileName(webGLFile)}[/] using Brotli compression.");
                                BrotliUtils.DecompressFile(webGLFile, rawWebGLFile);
                                break;
                            case ".data.gz":
                                AnsiConsole.MarkupLineInterpolated($"Decompress [green]{Path.GetFileName(webGLFile)}[/] using GZip compression.");
                                GZipUtils.DecompressFile(webGLFile, rawWebGLFile);
                                break;
                        }

                        // Unpack WebGL.data and set the gameDataDirectory to output folder
                        unpackedWebGLDirectory = UnityWebDataHelper.UnpackWebDataToFile(
                            bundleFile: rawWebGLFile
                        );
                        gameDataDirectory = unpackedWebGLDirectory;

                        break;
                    default:
                        AnsiConsole.MarkupLine("[red]Sorry, unsupported platform :([/]");
                        Console.ReadLine();
                        return;
                }
            }

            AssetsFileInstance? assetFileInstance = null;
            BundleFileInstance? bundleFileInstance = null;
            FileStream? bundleStream = null;

            globalgamemanagersFile = Path.Combine(gameDataDirectory, "globalgamemanagers");
            unity3dFile = Path.Combine(gameDataDirectory, "data.unity3d");

            try
            {
                if (File.Exists(globalgamemanagersFile))
                {
                    AnsiConsole.MarkupLine("Found [green]globalgamemanagers[/].");

                    // Make temporary copy, so the original file ready to be overwritten
                    string? tempFile = Utility.CloneFile(
                        globalgamemanagersFile,
                        $"{globalgamemanagersFile}.temp"
                    );
                    temporaryFiles.Add(tempFile);

                    AnsiConsole.MarkupLine("Loading [green]globalgamemanagers[/] and it's dependencies...");
                    assetFileInstance = assetsManager.LoadAssetsFile(tempFile, true);
                }

                if (File.Exists(unity3dFile))
                {
                    AnsiConsole.MarkupLine("Found [green]data.unity3d[/].");

                    string? tempFile = Utility.CloneFile(
                        unity3dFile,
                        $"{unity3dFile}.temp"
                    );
                    temporaryFiles.Add(tempFile);

                    AnsiConsole.MarkupLine("Unpacking [green]data.unity3d[/] file...");
                    string? unpackedBundleFile = $"{unity3dFile}.unpacked";
                    temporaryFiles.Add(unpackedBundleFile);

                    bundleStream = File.Open(unpackedBundleFile, FileMode.Create);
                    bundleFileInstance = assetsManager.LoadBundleFile(tempFile, false);
                    bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                        bundleFileInstance.file,
                        bundleStream
                    );

                    AnsiConsole.MarkupLine("Loading [green]globalgamemanagers[/] and it's dependencies...");
                    assetFileInstance = assetsManager.LoadAssetsFileFromBundle(
                        bundleFileInstance,
                        0,
                        true
                    );
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
            bool noWatermark = buildSettingsBase["isNoWatermarkBuild"].AsBool;

            if (isProVersion && !showUnityLogo)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Unity splash screen logo didn't exist or already removed. Nothing to do.[/]"
                );

                assetsManager.UnloadAll(true);
                Utility.CleanUp(temporaryFiles);

                if (isWebGL)
                {
                    Directory.Delete(gameDataDirectory, true);
                    if (webGLCompressionType != ".data")
                        File.Delete(rawWebGLFile);
                }

                Console.ReadLine();
                return;
            }

            AnsiConsole.MarkupLine(
                "[yellow]Sometimes USSR can\'t automatically detect Unity splash screen logo and it\'s leading to accidentally removing your own logo.[/] To tackle this, USSR needed information about \"Made With Unity\" logo duration."
            );
            AnsiConsole.MarkupLine(
                "[red]Please make a difference with the logo duration when you build your game! If your logo and Unity logo have same duration, USSR will remove both of them.[/] If no value provided, USSR will use it\'s own way to detect it and [red]may removing your own logo[/]."
            );
            AnsiConsole.Markup(
                "[green](Optional)[/] Enter Unity splash screen logo duration: "
            );

            int.TryParse(
                Console.ReadLine(),
                System.Globalization.NumberStyles.Integer,
                null,
                out int logoDuration
            );

            AnsiConsole.MarkupLine("Removing Unity splash screen...");

            // Remove Unity splash screen by flipping these boolean fields
            buildSettingsBase["hasPROVersion"].AsBool = !isProVersion; // true
            playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false
            buildSettingsBase["isNoWatermarkBuild"].AsBool = !noWatermark;

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

            AnsiConsole.MarkupLine("[green]Done.[/]");

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

                if (File.Exists(globalgamemanagersFile))
                {
                    Utility.BackupOnlyOnce(globalgamemanagersFile);

                    using AssetsFileWriter writer = new(globalgamemanagersFile);
                    assetFile?.Write(writer, 0, assetsReplacers);
                }

                if (File.Exists(unity3dFile))
                {
                    Utility.BackupOnlyOnce(unity3dFile);

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

                    string uncompressedBundleFile = $"{unity3dFile}.uncompressed";
                    temporaryFiles.Add(uncompressedBundleFile);

                    // Write modified assets to uncompressed asset bundle
                    using (AssetsFileWriter writer = new(uncompressedBundleFile))
                        bundleFile?.Write(writer, bundleReplacers);

                    // Compress asset bundle
                    using (
                        FileStream? uncompressedBundleStream = File.OpenRead(
                            uncompressedBundleFile
                        )
                    )
                    {
                        AnsiConsole.MarkupLine("Compressing [green]data.unity3d[/] file...");

                        AssetBundleFile? uncompressedBundle = new();
                        uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                        using AssetsFileWriter writer = new(unity3dFile);
                        uncompressedBundle.Pack(
                            uncompressedBundle.Reader,
                            writer,
                            AssetBundleCompressionType.LZ4
                        );
                    }
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

            if (isWebGL)
            {
                AnsiConsole.MarkupLine("Packing [green]WebGL[/] folder as [green]WebGL.data[/]...");

                // string? webGLdataPath = Path.Combine(execDirectory, "Build", "WebGL.data");
                UnityWebDataHelper.PackFilesToWebData(unpackedWebGLDirectory, rawWebGLFile);

                // Delete WebGL folder
                Directory.Delete(unpackedWebGLDirectory, true);

                // Compress WebGL.data if using compression
                switch (webGLCompressionType)
                {
                    case ".data.br":
                        AnsiConsole.MarkupLine("Compressing [green]WebGL.data[/] using Brotli compression. Please be patient, it might take some time...");
                        BrotliUtils.CompressFile(rawWebGLFile, $"{rawWebGLFile}.br");
                        break;
                    case ".data.gz":
                        AnsiConsole.MarkupLine("Compressing [green]WebGL.data[/] using GZip compression. Please be patient, it might take some time...");
                        GZipUtils.CompressFile(rawWebGLFile, $"{rawWebGLFile}.gz");
                        break;
                }

                if (webGLCompressionType != ".data")
                    File.Delete(rawWebGLFile);
            }

            AnsiConsole.MarkupLine(
                "[green]Successfully remove Unity splash screen.[/] Enjoy :) \n"
            );
            AnsiConsole.MarkupLine(
                "Don't forget to visit USSR repo ([link]https://github.com/kiraio-moe/USSR[/]) and give it a star!"
            );
            Console.ReadLine();
        }

        static void PrintHelp()
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[bold red]Unity Splash Screen Remover v{VERSION}[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "USSR is a CLI tool to easily remove Unity splash screen logo from your game and keeping your logo displayed. USSR didn't directly \"hack\" Unity Editor, but the generated build. So, not all platforms is supported."
            );
            AnsiConsole.MarkupLine("Before using USSR, make sure you have set splash screen \"Draw Mode\" in Player Settings to \"All Sequential\" and don't forget to backup your game files (USSR by default backuping your game files before doing it\'s job, but might be not because of bugs).");
            AnsiConsole.MarkupLine(
                "For more information, visit USSR GitHub repo: [link]https://github.com/kiraio-moe/USSR[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine("[bold green]Usages:[/]");
            AnsiConsole.MarkupLine(
                "Drag and drop your game executable [green](*.exe|*.x86|*.dmg|index.html)[/] to USSR.exe or use the following:"
            );
            AnsiConsole.MarkupLine("USSR.exe [green]<path/to/game.executable>[/]");
            AnsiConsole.MarkupLine("USSR.exe [green]<path/to/index.html>[/]");
        }

        static void LoadClassPackage(AssetsManager assetsManager, string tpkFile)
        {
            try
            {
                AnsiConsole.MarkupLine("Loading class type package...");
                assetsManager.LoadClassPackage(path: tpkFile);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Asset class types package not found![/] {ex.Message}"
                );
                Console.ReadLine();
                return;
            }
        }
    }
}
