using System;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Kiraio.UnityWebTools;
using NativeFileDialogSharp;
using Spectre.Console;
using USSR.Utilities;

namespace USSR.Core
{
    public class USSR
    {
        const string VERSION = "1.1.7";
        const string ASSET_CLASS_DB = "classdata.tpk";

        enum AssetTypes
        {
            Asset,
            Bundle
        }

        enum WebGLCompressionType
        {
            None,
            Brotli,
            GZip
        }

        static AssetTypes assetType;
        static WebGLCompressionType webGLCompressionType;

        static void Main(string[] args)
        {
            AnsiConsole.Background = Color.Grey11;
            PrintHelp();
            Console.WriteLine();

            string? ussrExec = Path.GetDirectoryName(AppContext.BaseDirectory);

            ChooseAction:
            string[] actionList = { "Remove Unity Splash Screen", "Remove Watermark", "Exit" };
            string actionPrompt = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do? (Press ENTER go, UP/DOWN to select)")
                    .AddChoices(actionList)
            );
            int choiceIndex = Array.FindIndex(actionList, item => item == actionPrompt);

            string? selectedFile = string.Empty,
                webDataFile = string.Empty,
                unpackedWebDataDirectory = string.Empty;
            bool isWebGL = false;

            switch (choiceIndex)
            {
                case 0:
                case 1:
                    AnsiConsole.MarkupLine("Opening File Picker...");
                    // Unfortunately, this File Picker library currently only support one filter :(
                    // So we pass all file types and manually checking them if it's a valid file that we want.
                    DialogResult filePicker = Dialog.FileOpen(
                        null,
                        Path.GetDirectoryName(Utility.GetLastOpenedFile())
                    );

                    if (filePicker.IsCancelled)
                    {
                        AnsiConsole.MarkupLine("Cancelled. Oh, it\'s okay ^_^");
                        Console.WriteLine();
                        goto ChooseAction;
                    }
                    else if (filePicker.IsError)
                    {
                        AnsiConsole.MarkupLine(
                            "[red]( RAWR )[/] Unable to open File Picker! Try using a different Terminal?"
                        );
                        Console.WriteLine();
                        goto ChooseAction;
                    }

                    selectedFile = filePicker.Path;
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Selected file: [green]{selectedFile}[/]"
                    );
                    Utility.SaveLastOpenedFile(selectedFile);
                    break;
                case 2:
                    AnsiConsole.MarkupLine("Have a nice day ;)");
                    Console.ReadLine();
                    return;
            }

            webDataFile = Path.Combine(
                Path.GetDirectoryName(selectedFile) ?? string.Empty,
                Path.GetFileNameWithoutExtension(selectedFile) // Without .br / .gz extension
            );
            string selectedFileName = Path.GetFileName(selectedFile);

            if (selectedFileName.Contains("globalgamemanagers"))
                assetType = AssetTypes.Asset;
            else if (selectedFileName.EndsWith(".unity3d"))
                assetType = AssetTypes.Bundle;
            else if (selectedFileName.EndsWith(".data"))
            {
                isWebGL = true;
                webDataFile = selectedFile;
                webGLCompressionType = WebGLCompressionType.None;
            }
            else if (selectedFileName.EndsWith("data.unityweb"))
            {
                string[] compressionList = { "Brotli", "GZip" };
                string compressionListPrompt = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("What compression type did you use?")
                        .AddChoices(compressionList)
                );
                int compressionChoiceIndex = Array.FindIndex(
                    compressionList,
                    item => item == compressionListPrompt
                );
                isWebGL = true;

                switch (compressionChoiceIndex)
                {
                    case 0:
                        webGLCompressionType = WebGLCompressionType.Brotli;
                        if (
                            DecompressCompressedWebData(
                                webGLCompressionType,
                                selectedFile,
                                webDataFile
                            ) == 1
                        )
                            goto ChooseAction;
                        break;
                    case 1:
                        webGLCompressionType = WebGLCompressionType.GZip;
                        if (
                            DecompressCompressedWebData(
                                webGLCompressionType,
                                selectedFile,
                                webDataFile
                            ) == 1
                        )
                            goto ChooseAction;
                        break;
                }
            }
            else if (selectedFileName.EndsWith("data.br"))
            {
                isWebGL = true;
                webGLCompressionType = WebGLCompressionType.Brotli;
                DecompressCompressedWebData(webGLCompressionType, selectedFile, webDataFile);
            }
            else if (selectedFileName.EndsWith("data.gz"))
            {
                isWebGL = true;
                webGLCompressionType = WebGLCompressionType.GZip;
                DecompressCompressedWebData(webGLCompressionType, selectedFile, webDataFile);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]( RAWR )[/] Unknown/Unsupported file type!");
                Console.WriteLine();
                goto ChooseAction;
            }

            AssetsManager assetsManager = new();
            string? tpkFile = Path.Combine(ussrExec ??= string.Empty, ASSET_CLASS_DB);
            LoadClassPackage(assetsManager, tpkFile);

            // List of files to be deleted later
            List<string> temporaryFiles = new();
            string inspectedFile = selectedFile;

            if (isWebGL)
            {
                // Unpack WebData asset + add to temporary files
                unpackedWebDataDirectory = UnityWebTool.Unpack(webDataFile);

                // Find and select "data.unity3d" or "globalgamemanagers"
                inspectedFile = Utility.FindRequiredAsset(unpackedWebDataDirectory);

                // Determine the asset type
                if (inspectedFile.Contains("globalgamemanagers"))
                    assetType = AssetTypes.Asset;
                else if (inspectedFile.EndsWith(".unity3d"))
                    assetType = AssetTypes.Bundle;
            }

            AssetsFileInstance? assetFileInstance = null;
            BundleFileInstance? bundleFileInstance = null;
            FileStream? bundleStream = null;
            List<AssetsReplacer>? assetsReplacer = null;

            string tempFile = Utility.CloneFile(inspectedFile, $"{inspectedFile}.temp");
            temporaryFiles.Add(tempFile);
            temporaryFiles.Add($"{tempFile}.unpacked"); // unpacked bundle file

            switch (assetType)
            {
                case AssetTypes.Asset:
                    assetFileInstance = LoadAssetFileInstance(tempFile, assetsManager);
                    break;
                case AssetTypes.Bundle:
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

            try
            {
                AnsiConsole.MarkupLine("( INFO ) Loading asset class types database...");
                assetsManager.LoadClassDatabaseFromPackage(
                    assetFileInstance?.file.Metadata.UnityVersion
                );
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Error when loading asset class types database! {ex.Message}"
                );
                goto Cleanup;
            }

            if (assetFileInstance != null)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) [bold]Unity Version[/]: [green]{assetFileInstance?.file.Metadata.UnityVersion.ToString()}[/]"
                );

                switch (choiceIndex)
                {
                    case 0:
                        assetsReplacer = RemoveSplashScreen(assetsManager, assetFileInstance);
                        break;
                    case 1:
                        assetsReplacer = RemoveWatermark(assetsManager, assetFileInstance);
                        break;
                }

                if (assetsReplacer != null)
                {
                    Utility.BackupOnlyOnce(selectedFile); // Backup original file
                    // Write changes to the asset file
                    WriteChanges(
                        inspectedFile,
                        assetFileInstance,
                        bundleFileInstance,
                        assetsReplacer
                    );
                }
            }

            Cleanup:
            bundleStream?.Close();
            assetsManager?.UnloadAll(true);
            Utility.CleanUp(temporaryFiles);

            // After writing the changes and cleaning the temporary files,
            // it's time to pack the extracted WebData.
            try
            {
                if (isWebGL && assetsReplacer != null)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Packing [green]{unpackedWebDataDirectory}[/]..."
                    );
                    switch (webGLCompressionType)
                    {
                        case WebGLCompressionType.Brotli:
                            UnityWebTool.Pack(unpackedWebDataDirectory, webDataFile);

                            AnsiConsole.MarkupLineInterpolated(
                                $"( INFO ) Compressing [green]{webDataFile}[/] using Brotli compression. Please be patient, it might take some time..."
                            );
                            BrotliUtils.CompressFile(webDataFile, selectedFile);
                            break;
                        case WebGLCompressionType.GZip:
                            UnityWebTool.Pack(unpackedWebDataDirectory, webDataFile);

                            AnsiConsole.MarkupLineInterpolated(
                                $"( INFO ) Compressing [green]{webDataFile}[/] using GZip compression. Please be patient, it might take some time..."
                            );
                            GZipUtils.CompressFile(webDataFile, selectedFile);
                            break;
                        case WebGLCompressionType.None:
                        default:
                            UnityWebTool.Pack(unpackedWebDataDirectory, selectedFile);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Error when compressing Unity Web Data! {ex.Message}"
                );
            }
            finally
            {
                if (isWebGL)
                {
                    try
                    {
                        if (Directory.Exists(unpackedWebDataDirectory))
                            Directory.Delete(unpackedWebDataDirectory, true);
                        if (
                            !webGLCompressionType.Equals(WebGLCompressionType.None)
                            && File.Exists(webDataFile)
                        )
                            File.Delete(webDataFile);
                    }
                    catch { }
                }
            }

            Console.WriteLine();
            goto ChooseAction;
        }

        static void PrintHelp()
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[bold red]Unity Splash Screen Remover v{VERSION}[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "USSR is a CLI tool to easily remove Unity splash screen logo (Made with Unity) from your game and keep your logo displayed. USSR didn't directly \"hack\" Unity Editor, but the generated build."
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "Before using USSR, make sure you have set the splash screen [bold green]\"Draw Mode\"[/] in [bold green]Player Settings[/] to [bold green]\"All Sequential\"[/] and don't forget to backup your game files!"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "For more information, visit USSR GitHub repo: [link]https://github.com/kiraio-moe/USSR[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine("[bold green]How to Use[/]:");
            AnsiConsole.MarkupLine(
                "Select the Action, find and choose one of these files in you game data:"
            );
            AnsiConsole.MarkupLine(
                "[green]globalgamemanagers[/] | [green]data.unity3d[/] | [green]<game_name>.data[/] | [green]<game_name>.data.br[/] | [green]<game_name>.data.gz[/] | [green]<game_name>.data.unityweb[/]"
            );
        }

        static void LoadClassPackage(AssetsManager assetsManager, string tpkFile)
        {
            if (File.Exists(tpkFile))
            {
                try
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"( INFO ) Loading class types package: [green]{tpkFile}[/]..."
                    );
                    assetsManager.LoadClassPackage(path: tpkFile);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[red]( RAWR )[/] Error when loading class types package! {ex.Message}"
                    );
                }
            }
            else
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] TPK file not found: [red]{tpkFile}[/]..."
                );
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
                        $"[red]( RAWR )[/] Error when loading asset file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Asset file not found: [red]{assetFile}[/]"
                );
            }

            return assetFileInstance;
        }

        /// <summary>
        /// Load AssetFileInstance from <paramref name="bundleFileInstance"/>.
        /// </summary>
        /// <param name="sourceFile"></param>
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
                        $"[red]( RAWR )[/] Error when loading asset file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Asset file not found: [red]{assetFile}[/]"
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
                    // It will throw an error if we use 'using'
                    unpackedBundleFileStream = File.Open($"{bundleFile}.unpacked", FileMode.Create);
                    bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                        bundleFileInstance.file,
                        unpackedBundleFileStream
                    );
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( RAWR )[/] Error when loading bundle file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Bundle file not found: [red]{bundleFile}[/]"
                );
            }

            return bundleFileInstance;
        }

        static List<AssetsReplacer>? RemoveSplashScreen(
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
                AssetTypeValueField? playerSettingsBase = assetsManager.GetBaseField(
                    assetFileInstance,
                    playerSettingsInfo?[0]
                );

                if (playerSettingsBase == null)
                {
                    AnsiConsole.MarkupLine(
                        "[red]( RAWR )[/] Can\'t get Player Settings fields! It\'s possible that this current version of Unity are currently not supported yet."
                    );
                    AnsiConsole.MarkupLine(
                        "Try updating USSR [bold green]classdata.tpk[/] manually from there: [link green]https://nightly.link/AssetRipper/Tpk/workflows/type_tree_tpk/master/uncompressed_file.zip[/] and try again."
                    );
                    AnsiConsole.MarkupLine(
                        "If the issue still persist, try switching to another Unity version."
                    );
                    return null;
                }

                // Required fields to remove splash screen
                bool hasProVersion = buildSettingsBase["hasPROVersion"].AsBool;
                bool showUnityLogo = playerSettingsBase["m_ShowUnitySplashLogo"].AsBool;

                // Check if the splash screen have been removed
                if (hasProVersion && !showUnityLogo)
                {
                    AnsiConsole.MarkupLine(
                        "[yellow]( WARN ) Unity splash screen already removed![/]"
                    );
                    return null;
                }

                AssetTypeValueField splashScreenLogos = playerSettingsBase[
                    "m_SplashScreenLogos.Array"
                ];
                int totalSplashScreen = splashScreenLogos.Count();
                int splashScreenIndex = 0;

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) There's [green]{totalSplashScreen}[/] splash screen detected."
                );

                switch (totalSplashScreen)
                {
                    case 0:
                        AnsiConsole.MarkupLine(
                            "[yellow]( WARN ) Nothing to do. Finally, taking a rest :)[/]"
                        );
                        return null;
                    case 1:
                        AnsiConsole.MarkupLine("( INFO ) Auto remove the splash screen...");
                        goto RemoveSplashScreen; // auto remove the splash screen
                }

                AnsiConsole.MarkupLine(
                    "What order are Unity splash screen logo in your Player Settings? (Start from 0 [upmost])"
                );

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
                        $"[red]( RAWR )[/] There's no splash screen at index [red]{splashScreenIndex}[/]! Try again."
                    );
                    goto InputLogoIndex;
                }

                RemoveSplashScreen:
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

                return new()
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
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Error when removing the splash screen! {ex.Message}"
                );
                return null;
            }
        }

        static List<AssetsReplacer>? RemoveWatermark(
            AssetsManager assetsManager,
            AssetsFileInstance? assetFileInstance
        )
        {
            try
            {
                AnsiConsole.MarkupLine("( INFO ) Removing watermark...");

                AssetsFile? assetFile = assetFileInstance?.file;
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
                    return null;
                }

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Set [green]isNoWatermarkBuild = {!noWatermark}[/] | [green]isTrial = {!isTrial}[/]"
                );
                buildSettingsBase["isNoWatermarkBuild"].AsBool = true;
                buildSettingsBase["isTrial"].AsBool = false;

                AnsiConsole.MarkupLine("( INFO ) [green]Watermark successfully removed.[/]");
                return new()
                {
                    new AssetsReplacerFromMemory(
                        assetFile,
                        buildSettingsInfo?[0],
                        buildSettingsBase
                    )
                };
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( RAWR )[/] Error when removing the watermark! {ex.Message}"
                );
                return null;
            }
        }

        static void WriteChanges(
            string modifiedFile,
            AssetsFileInstance? assetFileInstance,
            BundleFileInstance? bundleFileInstance,
            List<AssetsReplacer> assetsReplacer
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
                        assetFileInstance?.file.Write(writer, 0, assetsReplacer);
                        break;
                    }
                    case AssetTypes.Bundle:
                    {
                        List<BundleReplacer> bundleReplacer =
                            new()
                            {
                                new BundleReplacerFromAssets(
                                    assetFileInstance?.name,
                                    null,
                                    assetFileInstance?.file,
                                    assetsReplacer
                                )
                            };

                        // Write modified assets to uncompressed asset bundle
                        using (AssetsFileWriter writer = new(uncompressedBundleFile))
                            bundleFileInstance?.file.Write(writer, bundleReplacer);

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
                                uncompressedBundle.Reader,
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
                    $"[red]( RAWR )[/] Error when writing changes! {ex.Message}"
                );
            }
            finally
            {
                if (File.Exists(uncompressedBundleFile))
                    File.Delete(uncompressedBundleFile);
            }
        }

        static int DecompressCompressedWebData(
            WebGLCompressionType compressionType,
            string inputPath,
            string outputPath
        )
        {
            switch (compressionType)
            {
                case WebGLCompressionType.Brotli:
                    try
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"( INFO ) Decompressing Brotli [green]{inputPath}[/]..."
                        );
                        BrotliUtils.DecompressFile(inputPath, outputPath);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[red]( RAWR ) Failed to decompress {inputPath}![/] {ex.Message} Try choose different compression type."
                        );
                        Console.WriteLine();
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                        return 1;
                    }
                case WebGLCompressionType.GZip:
                    try
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"( INFO ) Decompressing GZip [green]{inputPath}[/]..."
                        );
                        GZipUtils.DecompressFile(inputPath, outputPath);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[red]( RAWR ) Failed to decompress {inputPath}![/] {ex.Message} Try choose different compression type."
                        );
                        Console.WriteLine();
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                        return 1;
                    }
                case WebGLCompressionType.None:
                default:
                    return 1;
            }
        }
    }
}
