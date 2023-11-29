using System.Reflection;
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
        const string VERSION = "1.1.6";
        const string ASSET_CLASS_DB = "classdata.tpk";
        static readonly byte[] ggmMagic =
        {
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x16,
            0x00,
            0x00,
            0x00,
            0x00
        };

        static readonly byte[] unity3dMagic =
        {
            0x55,
            0x6E,
            0x69,
            0x74,
            0x79,
            0x46,
            0x53,
            0x00,
            0x00,
            0x00,
            0x00,
            0x08,
            0x35,
            0x2E,
            0x78,
            0x2E
        };

        static readonly byte[] unityWebDataMagic =
        {
            0x55,
            0x6E,
            0x69,
            0x74,
            0x79,
            0x57,
            0x65,
            0x62,
            0x44,
            0x61,
            0x74,
            0x61,
            0x31,
            0x2E,
            0x30,
            0x00
        };

        static readonly byte[] gzipMagic = { 0x1f, 0x8b };

        // Ref: https://github.com/google/brotli/issues/867#issue-739852869
        // I have already test this magic bytes into an asset file, but
        // it's turning out that it's not work.
        static readonly byte[] unityBrotliMagic =
        {
            0x6B,
            0x8D,
            0x00,
            0x55,
            0x6E,
            0x69,
            0x74,
            0x79,
            0x57,
            0x65,
            0x62,
            0x20,
            0x43,
            0x6F,
            0x6D,
            0x70,
            0x72,
            0x65,
            0x73,
            0x73,
            0x65,
            0x64,
            0x20,
            0x43,
            0x6F,
            0x6E,
            0x74,
            0x65,
            0x6E,
            0x74,
            0x20,
            0x28,
            0x62,
            0x72,
            0x6F,
            0x74,
            0x6C,
            0x69,
            0x29
        };

        enum AssetTypes
        {
            Asset,
            Bundle
        }

        static AssetTypes assetType;

        enum WebGLCompressionType
        {
            None,
            Brotli,
            GZip
        }

        static WebGLCompressionType webGLCompressionType;

        static void Main(string[] args)
        {
            AnsiConsole.Background = Color.Grey11;
            PrintHelp();
            Console.WriteLine();

            string? ussrExec = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ChooseAction:
            string[] actionList = { "Remove Unity Splash Screen", "Remove Watermark", "Exit" };
            string actionPrompt = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .AddChoices(actionList)
            );
            int choiceIndex = Array.FindIndex(actionList, item => item == actionPrompt);

            string? selectedFile = string.Empty,
                originalFileName = string.Empty;
            string? webDataFile = string.Empty,
                unpackedWebDataDirectory = string.Empty;
            bool isWebGL = false;

            switch (choiceIndex)
            {
                case 0:
                case 1:
                    // Unfortunately, only one filter are supported.
                    // Instead of working around with this, we just need to manually validate by reading the file header later.
                    DialogResult filePicker = Dialog.FileOpen(
                        null,
                        Path.GetDirectoryName(Utility.GetLastOpenedFile())
                    );

                    if (filePicker.IsCancelled)
                        goto ChooseAction;
                    else if (filePicker.IsError)
                    {
                        AnsiConsole.MarkupLine(
                            "[red]( ERROR )[/]Unable to open File Picker dialog!"
                        );
                        goto ChooseAction;
                    }

                    selectedFile = originalFileName = filePicker.Path;
                    Utility.SaveLastOpenedFile(selectedFile);
                    break;
                case 2:
                    AnsiConsole.MarkupLine("Have a nice day ;)");
                    Console.ReadLine();
                    return;
            }

            webDataFile = Path.Combine(
                Path.GetDirectoryName(selectedFile) ?? string.Empty,
                Path.GetFileNameWithoutExtension(selectedFile)
            );

            if (Utility.ValidateFile(selectedFile, ggmMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]globalgamemanagers[/] file selected.");
                assetType = AssetTypes.Asset;
            }
            else if (Utility.ValidateFile(selectedFile, unity3dMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]unity3d[/] file selected.");
                assetType = AssetTypes.Bundle;
            }
            else if (Utility.ValidateFile(selectedFile, unityWebDataMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]UnityWebData[/] file selected.");
                isWebGL = true;
                webDataFile = selectedFile;
                webGLCompressionType = WebGLCompressionType.None;
            }
            else if (
                Utility.ValidateFile(selectedFile, unityBrotliMagic)
                || Path.GetExtension(selectedFile) == ".br"
            )
            {
                AnsiConsole.MarkupLine("( INFO ) [green]UnityWebData Brotli[/] file selected.");
                isWebGL = true;
                webGLCompressionType = WebGLCompressionType.Brotli;

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Decompressing [green]{selectedFile}[/]..."
                );
                BrotliUtils.DecompressFile(selectedFile, webDataFile);
            }
            else if (Utility.ValidateFile(selectedFile, gzipMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]UnityWebData GZip[/] file selected.");
                isWebGL = true;
                webGLCompressionType = WebGLCompressionType.GZip;

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Decompressing [green]{selectedFile}[/]..."
                );
                GZipUtils.DecompressFile(selectedFile, webDataFile);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]( ERROR )[/] Unknown/Unsupported file type!");
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
                else if (inspectedFile.Contains("unity3d"))
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
                    $"[red]( ERROR )[/] Error when loading asset class types database! {ex.Message}"
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
                    switch (webGLCompressionType)
                    {
                        case WebGLCompressionType.Brotli:
                            UnityWebTool.Pack(unpackedWebDataDirectory, webDataFile);

                            AnsiConsole.MarkupLineInterpolated(
                                $"( INFO ) Compressing [green]{webDataFile}[/] using Brotli compression. Please be patient, it might take some time..."
                            );
                            BrotliUtils.CompressFile(webDataFile, selectedFile);
                            // BrotliUtils.WriteUnityIdentifier(selectedFile, unityBrotliMagic);

                            if (File.Exists(webDataFile))
                                File.Delete(webDataFile);
                            break;
                        case WebGLCompressionType.GZip:
                            UnityWebTool.Pack(unpackedWebDataDirectory, webDataFile);

                            AnsiConsole.MarkupLineInterpolated(
                                $"( INFO ) Compressing [green]{webDataFile}[/] using GZip compression. Please be patient, it might take some time..."
                            );
                            GZipUtils.CompressFile(webDataFile, selectedFile);

                            if (File.Exists(webDataFile))
                                File.Delete(webDataFile);
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
                    $"[red]( ERROR )[/] Error when compressing Unity Web Data! {ex.Message}"
                );
            }
            finally
            {
                if (Directory.Exists(unpackedWebDataDirectory))
                    Directory.Delete(unpackedWebDataDirectory, true);
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
                "[green]globalgamemanagers[/] | [green]data.unity3d[/] | [green]<game_name>.data[/] | [green]<game_name>.data.br[/] | [green]<game_name>.data.gz[/]"
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
                        $"[red]( ERROR )[/] Error when loading class types package! {ex.Message}"
                    );
                }
            }
            else
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERROR )[/] TPK file not found: [red]{tpkFile}[/]..."
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
                        $"[red]( ERROR )[/] Error when loading asset file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERROR )[/] Asset file not found: [red]{assetFile}[/]"
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
                        $"[red]( ERROR )[/] Error when loading asset file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERROR )[/] Asset file not found: [red]{assetFile}[/]"
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
                        $"[red]( ERROR )[/] Error when loading bundle file! {ex.Message}"
                    );
                }
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]( ERROR )[/] Bundle file not found: [red]{bundleFile}[/]"
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
                        "[red]( ERROR )[/] Can\'t get Player Settings fields! It\'s possible that this current version of Unity are currently not supported yet."
                    );
                    AnsiConsole.MarkupLine(
                        "Try updating USSR [bold green]classdata.tpk[/] from there: [link green]https://nightly.link/AssetRipper/Tpk/workflows/type_tree_tpk/master/uncompressed_file.zip[/] and try again."
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
                        "[yellow]( WARN ) Unity splash screen have been removed![/]"
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
                            "[yellow]Did you set the splash screen [bold]Draw Mode[/] to [bold]Unity Logo Below[/]? That\'s useless..[/]"
                        );
                        return null;
                    case 1:
                        AnsiConsole.MarkupLine("( INFO ) Auto remove the splash screen...");
                        goto RemoveSplashScreen; // auto remove the splash screen
                }

                AnsiConsole.MarkupLine(
                    "What order are Unity splash screen logo in your Player Settings? (Start from 0)"
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
                        $"[red]( ERROR )[/] There's no splash screen at index [red]{splashScreenIndex}[/]! Try again."
                    );
                    goto InputLogoIndex;
                }

                RemoveSplashScreen:
                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Set [green]hasProVersion = {!hasProVersion}[/] | [green]m_ShowUnitySplashLogo = {!showUnityLogo}[/]"
                );

                // Remove Unity splash screen by flipping these boolean fields
                buildSettingsBase["hasPROVersion"].AsBool = !hasProVersion; // true
                playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) [green]Removed splash screen at index {splashScreenIndex}.[/]"
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
                    $"[red]( ERROR )[/] Error when removing the splash screen! {ex.Message}"
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
                    $"[red]( ERROR )[/] Error when removing the watermark! {ex.Message}"
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
                    $"[red]( ERROR )[/] Error when writing changes! {ex.Message}"
                );
            }
            finally
            {
                if (File.Exists(uncompressedBundleFile))
                    File.Delete(uncompressedBundleFile);
            }
        }
    }
}
