using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Kiraio.UnityWebTools;
using NativeFileDialogSharp;
using Spectre.Console;
using USSR.Enums;
using USSR.Utilities;

namespace USSR.Core
{
    public class USSR
    {
        static readonly string? appVersion = Utility.GetVersion();
        const string ASSET_CLASS_DB = "classdata.tpk";

        static void Main(string[] args)
        {
            Console.Title = $"Unity Splash Screen Remover v{appVersion}";
            AnsiConsole.Background = Color.Grey11;

            while (true)
            {
                PrintHelp();
                Console.WriteLine();

                string? ussrExec = Path.GetDirectoryName(AppContext.BaseDirectory);
                int choiceIndex = GetChoice();
                if (choiceIndex == 2)
                    return;
                string? selectedFile = OpenFilePicker();

                if (selectedFile == null)
                {
                    continue; // Prompt for action again
                }

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Selected file: [green]{selectedFile}[/]"
                );
                Utility.SaveLastOpenedFile(selectedFile);

                string webDataFile = Path.Combine(
                    Path.GetDirectoryName(selectedFile) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(selectedFile)
                );
                string? unpackedWebDataDirectory = string.Empty;
                bool isWebGL = false;

                AssetTypes assetType = GetAssetType(selectedFile, ref webDataFile, ref isWebGL);
                if (assetType == AssetTypes.Unknown)
                {
                    AnsiConsole.MarkupLine("[red]( ERR! )[/] Unknown/Unsupported file type!");
                    Console.WriteLine();
                    continue; // Prompt for action again
                }

                AssetsManager assetsManager = new();
                string? tpkFile = Path.Combine(ussrExec ?? string.Empty, ASSET_CLASS_DB);
                if (!LoadClassPackage(assetsManager, tpkFile))
                {
                    continue; // Prompt for action again
                }

                List<string> temporaryFiles = new();
                string inspectedFile = selectedFile;

                if (isWebGL)
                {
                    unpackedWebDataDirectory = UnityWebTool.Unpack(webDataFile);
                    inspectedFile = Utility.FindRequiredAsset(unpackedWebDataDirectory);
                    assetType = GetAssetType(inspectedFile, ref webDataFile, ref isWebGL);
                }

                AssetsFileInstance? assetFileInstance = null;
                BundleFileInstance? bundleFileInstance = null;
                FileStream? bundleStream = null;

                string tempFile = Utility.CloneFile(inspectedFile, $"{inspectedFile}.temp");
                temporaryFiles.Add(tempFile);
                temporaryFiles.Add($"{tempFile}.unpacked");

                switch (assetType)
                {
                    case AssetTypes.Asset:
                        assetFileInstance = LoadAssetFileInstance(tempFile, assetsManager);
                        break;
                    case AssetTypes.Bundle:
                        bundleStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read);
                        bundleFileInstance = LoadBundleFileInstance(
                            tempFile,
                            assetsManager,
                            bundleStream
                        );
                        assetFileInstance = LoadAssetFileInstance(
                            tempFile,
                            assetsManager,
                            bundleFileInstance
                        );
                        break;
                }

                if (assetFileInstance != null)
                {
                    try
                    {
                        AnsiConsole.MarkupLine("( INFO ) Loading asset class types database...");
                        assetsManager.LoadClassDatabaseFromPackage(
                            assetFileInstance.file.Metadata.UnityVersion
                        );
                        AnsiConsole.MarkupLineInterpolated(
                            $"( INFO ) Unity Version: [bold green]{assetFileInstance.file.Metadata.UnityVersion}[/]"
                        );

                        switch (choiceIndex)
                        {
                            case 0:
                                assetFileInstance.file = RemoveSplashScreen(
                                    assetsManager,
                                    assetFileInstance
                                );
                                break;
                            case 1:
                                assetFileInstance.file = RemoveWatermark(
                                    assetsManager,
                                    assetFileInstance
                                );
                                break;
                        }

                        if (assetFileInstance.file != null)
                        {
                            Utility.BackupOnlyOnce(selectedFile);
                            WriteChanges(
                                inspectedFile,
                                assetType,
                                assetFileInstance,
                                bundleFileInstance
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[red]( ERR! )[/] Error when loading asset class types database! {ex.Message}"
                        );
                        continue; // Prompt for action again
                    }
                }

                Cleanup(
                    temporaryFiles,
                    bundleStream,
                    assetsManager,
                    unpackedWebDataDirectory,
                    webDataFile,
                    isWebGL
                );

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("Press any key to continue...");
                Console.ReadKey();
                Console.Clear();
            }
        }

        static AssetTypes GetAssetType(
            string selectedFile,
            ref string webDataFile,
            ref bool isWebGL
        )
        {
            string selectedFileName = Path.GetFileName(selectedFile);
            if (selectedFileName.Contains("globalgamemanagers"))
                return AssetTypes.Asset;
            if (selectedFileName.EndsWith(".unity3d"))
                return AssetTypes.Bundle;
            if (selectedFileName.EndsWith(".data"))
            {
                isWebGL = true;
                webDataFile = selectedFile;
                return AssetTypes.Asset; // Default to Asset type
            }
            if (selectedFileName.EndsWith("data.unityweb"))
            {
                isWebGL = true;
                WebCompressionTypes webCompressionType = GetCompression();
                if (!DecompressWebData(webCompressionType, selectedFile, webDataFile))
                    return AssetTypes.Unknown;
                return AssetTypes.Asset;
            }
            if (selectedFileName.EndsWith("data.br"))
            {
                isWebGL = true;
                DecompressWebData(WebCompressionTypes.Brotli, selectedFile, webDataFile);
                return AssetTypes.Asset;
            }
            if (selectedFileName.EndsWith("data.gz"))
            {
                isWebGL = true;
                DecompressWebData(WebCompressionTypes.GZip, selectedFile, webDataFile);
                return AssetTypes.Asset;
            }
            return AssetTypes.Unknown;
        }

        static void Cleanup(
            List<string> temporaryFiles,
            FileStream? bundleStream,
            AssetsManager assetsManager,
            string? unpackedWebDataDirectory,
            string webDataFile,
            bool isWebGL
        )
        {
            bundleStream?.Close();
            assetsManager?.UnloadAll(true);
            Utility.CleanUp(temporaryFiles);

            if (isWebGL)
            {
                try
                {
                    if (Directory.Exists(unpackedWebDataDirectory))
                        Directory.Delete(unpackedWebDataDirectory, true);
                    if (File.Exists(webDataFile))
                        File.Delete(webDataFile);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]( ERR! )[/] Error cleaning up temporary files!");
                    AnsiConsole.WriteException(ex);
                }
            }
        }

        static void PrintHelp()
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[bold red]Unity Splash Screen Remover v{appVersion}[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "USSR is a tool to easily remove Unity splash screen. USSR didn't directly \"hack\" the Unity Editor, but the generated build."
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "Before using USSR, make sure you have set the splash screen [bold green]Draw Mode[/] in [bold green]Player Settings[/] to [bold green]All Sequential[/] and backup the target file below! For more information, visit USSR GitHub repo: [link]https://github.com/kiraio-moe/USSR[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine("[bold green]HOW TO USE[/]");
            AnsiConsole.MarkupLine(
                "Select the action below, find and select one of these files in your game data:"
            );
            AnsiConsole.MarkupLine(
                "[green]globalgamemanagers[/] | [green]data.unity3d[/] | [green]*.data[/] | [green]*.data.br[/] | [green]*.data.gz[/] | [green]*.data.unityweb[/]"
            );
            Console.WriteLine();
        }

        static int GetChoice()
        {
            string[] menuList = { "Remove Unity Splash Screen", "Remove Watermark", "Exit" };
            string actionPrompt = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do? (Press ENTER to go, UP/DOWN to select)")
                    .AddChoices(menuList)
            );
            return Array.FindIndex(menuList, item => item == actionPrompt);
        }

        static WebCompressionTypes GetCompression()
        {
            string[] compressionList = { "Brotli", "GZip" };
            string compressionListPrompt = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What compression did you use?")
                    .AddChoices(compressionList)
            );
            int choiceIndex = Array.FindIndex(
                compressionList,
                item => item == compressionListPrompt
            );

            return choiceIndex switch
            {
                0 => WebCompressionTypes.Brotli,
                1 => WebCompressionTypes.GZip,
                _ => WebCompressionTypes.None,
            };
        }

        static string OpenFilePicker()
        {
            AnsiConsole.MarkupLine("Opening File Picker...");

            DialogResult filePicker = Dialog.FileOpen(
                null,
                Path.GetDirectoryName(Utility.GetLastOpenedFile())
            );

            if (filePicker.IsCancelled)
                AnsiConsole.MarkupLine("( INFO ) Cancelled.");
            else if (filePicker.IsError)
            {
                AnsiConsole.MarkupLine(
                    "[red]( ERR! )[/] Unable to open File Picker! Try using a different Terminal?"
                );
            }

            Console.WriteLine();
            Console.Clear();

            return filePicker.Path;
        }

        static bool LoadClassPackage(AssetsManager assetsManager, string tpkFile)
        {
            if (File.Exists(tpkFile))
            {
                try
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Loading class types package: [green]{tpkFile}[/]..."
                    );
                    assetsManager.LoadClassPackage(path: tpkFile);
                    return true;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[red]( ERR! )[/] Error when loading class types package! {ex.Message}"
                    );
                }
            }
            else
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] TPK file not found: [red]{tpkFile}[/]..."
                );

            return false;
        }

        /// <summary>
        /// Load AssetFileInstance.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        static AssetsFileInstance? LoadAssetFileInstance(
            string assetFile,
            AssetsManager assetsManager
        )
        {
            AssetsFileInstance? assetFileInstance = null;

            if (File.Exists(assetFile))
            {
                try
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Loading asset file: [green]{assetFile}[/]..."
                    );
                    assetFileInstance = assetsManager.LoadAssetsFile(assetFile, true);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERR! )[/] Error when loading asset file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] Asset file not found: [red]{assetFile}[/]"
                );
            }

            return assetFileInstance;
        }

        /// <summary>
        /// Load AssetFileInstance from <paramref name="bundleFileInstance"/>.
        /// </summary>
        /// <param name="assetFile"></param>
        /// <param name="assetsManager"></param>
        /// <param name="bundleFileInstance"></param>
        /// <returns></returns>
        static AssetsFileInstance? LoadAssetFileInstance(
            string assetFile,
            AssetsManager assetsManager,
            BundleFileInstance? bundleFileInstance
        )
        {
            AssetsFileInstance? assetFileInstance = null;

            if (File.Exists(assetFile))
            {
                try
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Loading asset file: [green]{assetFile}[/]..."
                    );
                    assetFileInstance = assetsManager.LoadAssetsFileFromBundle(
                        bundleFileInstance,
                        0,
                        true
                    );
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERR! )[/] Error when loading asset file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] Asset file not found: [red]{assetFile}[/]"
                );
            }

            return assetFileInstance;
        }

        static BundleFileInstance? LoadBundleFileInstance(
            string bundleFile,
            AssetsManager assetsManager,
            FileStream? unpackedBundleFileStream
        )
        {
            BundleFileInstance? bundleFileInstance = null;

            if (File.Exists(bundleFile))
            {
                try
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Loading bundle file: [green]{bundleFile}[/]..."
                    );
                    bundleFileInstance = assetsManager.LoadBundleFile(bundleFile, false);
                    //! Don't auto dispose the stream
                    unpackedBundleFileStream = File.Open($"{bundleFile}.unpacked", FileMode.Create);
                    bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                        bundleFileInstance.file,
                        unpackedBundleFileStream
                    );
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERR! )[/] Error when loading bundle file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] Bundle file not found: [red]{bundleFile}[/]"
                );
            }

            return bundleFileInstance;
        }

        static AssetsFile? RemoveSplashScreen(
            AssetsManager assetsManager,
            AssetsFileInstance? assetFileInstance
        )
        {
            try
            {
                AnsiConsole.MarkupLine("( INFO ) Start removing Unity splash screen...");

                AssetsFile? assetFile = assetFileInstance?.file;

                List<AssetFileInfo>? buildSettingsInfo = assetFile?.GetAssetsOfType(
                    AssetClassID.BuildSettings
                );
                AssetTypeValueField buildSettingsBase = assetsManager.GetBaseField(
                    assetFileInstance,
                    buildSettingsInfo?[0]
                );

                List<AssetFileInfo>? playerSettingsInfo = assetFile?.GetAssetsOfType(
                    AssetClassID.PlayerSettings
                );
                AssetTypeValueField? playerSettingsBase = null;
                try
                {
                    playerSettingsBase = assetsManager.GetBaseField(
                        assetFileInstance,
                        playerSettingsInfo?[0]
                    );
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERR! )[/] Can\'t get Player Settings fields! {ex.Message} It\'s possible that the current Unity version isn\'t supported yet."
                    );
                    AnsiConsole.MarkupLine(
                        "( INFO ) Try updating the [bold green]classdata.tpk[/] manually from there: [link green]https://nightly.link/AssetRipper/Tpk/workflows/type_tree_tpk/master/uncompressed_file.zip[/] and try again. If the issue still persist, try use another Unity version."
                    );
                    return assetFile;
                }

                // Required fields to remove splash screen
                bool hasProVersion = buildSettingsBase["hasPROVersion"].AsBool;
                bool showUnityLogo = playerSettingsBase["m_ShowUnitySplashLogo"].AsBool;

                // Check if the splash screen have been removed
                if (hasProVersion && !showUnityLogo)
                {
                    AnsiConsole.MarkupLine(
                        "[yellow]( WARN ) [bold]Unity splash screen already removed![/][/]"
                    );
                    return assetFile;
                }

                AssetTypeValueField splashScreenLogos = playerSettingsBase[
                    "m_SplashScreenLogos.Array"
                ];
                int totalSplashScreen = splashScreenLogos.Count();
                int splashScreenIndex = 0;

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) There's [green]{totalSplashScreen}[/] splash screen detected."
                );

                if (totalSplashScreen <= 0)
                {
                    AnsiConsole.MarkupLine("[yellow]( WARN ) Nothing to do.[/]");
                    return assetFile;
                }

                for (int i = 0; i < totalSplashScreen; i++)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[green]{i}[/] => [green]{splashScreenLogos.Children[i].FieldName}[/]"
                    );
                }

                AnsiConsole.MarkupLine("What order are Unity logo in your Player Settings?");

                InputLogoIndex:
                int.TryParse(
                    Console.ReadLine(),
                    System.Globalization.NumberStyles.Integer,
                    null,
                    out splashScreenIndex
                );

                if (splashScreenIndex < 0 && splashScreenIndex >= totalSplashScreen)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERR! )[/] There's no splash screen at index [red]{splashScreenIndex}[/]! Try again."
                    );
                    goto InputLogoIndex;
                }

                // RemoveSplashScreen:
                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Set [green]hasProVersion[/] = [green]{!hasProVersion}[/] | [green]m_ShowUnitySplashLogo[/] = [green]{!showUnityLogo}[/]"
                );

                // Remove Unity splash screen by flipping these boolean fields
                buildSettingsBase["hasPROVersion"].AsBool = !hasProVersion; // true
                playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) [green]Splash screen removed at index {splashScreenIndex}.[/]"
                );

                splashScreenLogos?.Children.RemoveAt(splashScreenIndex);
                playerSettingsInfo?[0].SetNewData(playerSettingsBase);
                buildSettingsInfo?[0].SetNewData(buildSettingsBase);

                return assetFile;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] Error when removing the splash screen! {ex.Message}"
                );
                return null;
            }
        }

        static AssetsFile? RemoveWatermark(
            AssetsManager assetsManager,
            AssetsFileInstance? assetFileInstance
        )
        {
            AssetsFile? assetFile = assetFileInstance?.file;
            try
            {
                AnsiConsole.MarkupLine("( INFO ) Removing watermark...");
                List<AssetFileInfo>? buildSettingsInfo = assetFile?.GetAssetsOfType(
                    AssetClassID.BuildSettings
                );
                AssetTypeValueField buildSettingsBase = assetsManager.GetBaseField(
                    assetFileInstance,
                    buildSettingsInfo?[0]
                );

                bool noWatermark = buildSettingsBase["isNoWatermarkBuild"].AsBool;
                bool isTrial = buildSettingsBase["isTrial"].AsBool;

                if (noWatermark && !isTrial)
                {
                    AnsiConsole.MarkupLine("[yellow]( WARN ) Watermark have been removed![/]");
                    return assetFile;
                }

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Set [green]isNoWatermarkBuild[/] = [green]True[/] | [green]isTrial[/] = [green]False[/]"
                );

                buildSettingsBase["isNoWatermarkBuild"].AsBool = true;
                buildSettingsBase["isTrial"].AsBool = false;
                buildSettingsInfo?[0].SetNewData(buildSettingsBase);

                AnsiConsole.MarkupLine("[green]( INFO ) Watermark successfully removed.[/]");
                return assetFile;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] Error when removing the watermark! {ex.Message}"
                );
                return assetFile;
            }
        }

        static void WriteChanges(
            string modifiedFile,
            AssetTypes assetType,
            AssetsFileInstance? assetFileInstance,
            BundleFileInstance? bundleFileInstance
        )
        {
            string uncompressedBundleFile = $"{modifiedFile}.uncompressed";

            try
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Writing changes to [green]{modifiedFile}[/]..."
                );

                switch (assetType)
                {
                    case AssetTypes.Asset:
                    {
                        using AssetsFileWriter writer = new(modifiedFile);
                        assetFileInstance?.file.Write(writer);
                        break;
                    }
                    case AssetTypes.Bundle:
                    {
                        List<ContentReplacerFromAssets> bundleReplacer =
                            new() { new ContentReplacerFromAssets(assetFileInstance?.file) };

                        // Write modified assets to uncompressed asset bundle
                        using (AssetsFileWriter writer = new(uncompressedBundleFile))
                            bundleFileInstance?.file.Write(writer);

                        AnsiConsole.MarkupLineInterpolated(
                            $"( INFO ) Compressing [green]{modifiedFile}[/]..."
                        );
                        using (
                            FileStream uncompressedBundleStream = File.OpenRead(
                                uncompressedBundleFile
                            )
                        )
                        {
                            AssetBundleFile uncompressedBundle = new();
                            uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                            using AssetsFileWriter uncompressedWriter = new(modifiedFile);
                            uncompressedBundle.Pack(
                                uncompressedWriter,
                                AssetBundleCompressionType.LZ4
                            );
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! )[/] Error when writing changes! {ex.Message}"
                );
            }
            finally
            {
                if (File.Exists(uncompressedBundleFile))
                    File.Delete(uncompressedBundleFile);
            }
        }

        static bool DecompressWebData(
            WebCompressionTypes compressionType,
            string inputPath,
            string outputPath
        )
        {
            AnsiConsole.MarkupLineInterpolated(
                $"( INFO ) Decompressing data as {compressionType.ToString()} compression: [green]{inputPath}[/]..."
            );

            try
            {
                switch (compressionType)
                {
                    case WebCompressionTypes.Brotli:
                        BrotliUtils.DecompressFile(inputPath, outputPath);
                        return true;
                    case WebCompressionTypes.GZip:
                        GZipUtils.DecompressFile(inputPath, outputPath);
                        return true;
                    case WebCompressionTypes.None:
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERR! ) Failed to decompress: {inputPath}![/] {ex.Message} Try different compression type."
                );
                Console.WriteLine();

                return false;
            }
        }
    }
}
