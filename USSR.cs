﻿using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
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

        // Luckily, Unity have customized Brotli specification, so we can detect it.
        // Ref: https://github.com/google/brotli/issues/867#issue-739852869
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

        enum WebGLCompressionTypes
        {
            Brotli,
            GZip
        }

        static WebGLCompressionTypes webGLCompressionType;

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
            }
            else if (Utility.ValidateFile(selectedFile, unityBrotliMagic) || Path.GetExtension(selectedFile) == ".br")
            {
                AnsiConsole.MarkupLine("( INFO ) [green]UnityWebData Brotli[/] file selected.");
                isWebGL = true;
                webGLCompressionType = WebGLCompressionTypes.Brotli;

                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Decompressing [green]{selectedFile}[/]..."
                );
                BrotliUtils.DecompressFile(selectedFile, webDataFile);
            }
            else if (Utility.ValidateFile(selectedFile, gzipMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]UnityWebData GZip[/] file selected.");
                isWebGL = true;
                webGLCompressionType = WebGLCompressionTypes.GZip;

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
                unpackedWebDataDirectory = UnityWebDataHelper.UnpackWebDataToFile(webDataFile);

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

            AnsiConsole.MarkupLine("( INFO ) Loading asset class types database...");
            if (assetFileInstance != null)
                assetsManager.LoadClassDatabaseFromPackage(
                    assetFileInstance?.file.Metadata.UnityVersion
                );
            else
                AnsiConsole.MarkupLine(
                    "[red]( ERROR )[/] Unable to load asset class types database!"
                );

            List<AssetsReplacer>? assetsReplacer = null;

            if (assetFileInstance != null)
            {
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

            bundleStream?.Close();
            assetsManager?.UnloadAll(true);
            Utility.CleanUp(temporaryFiles);

            // After writing the changes and cleaning the temporary files,
            // it's time to pack the extracted WebData back
            if (isWebGL)
            {
                // Only pack if the contents is modified
                if (assetsReplacer != null)
                {
                    if (
                        webGLCompressionType == WebGLCompressionTypes.Brotli
                        || webGLCompressionType == WebGLCompressionTypes.GZip
                    )
                        UnityWebDataHelper.PackFilesToWebData(
                            unpackedWebDataDirectory,
                            webDataFile
                        );

                    switch (webGLCompressionType)
                    {
                        case WebGLCompressionTypes.Brotli:
                            AnsiConsole.MarkupLineInterpolated(
                                $"( INFO ) Compressing [green]{webDataFile}[/] using Brotli compression. Please be patient, it might take some time..."
                            );
                            BrotliUtils.CompressFile(webDataFile, selectedFile);
                            // BrotliUtils.WriteUnityIdentifier(selectedFile, unityBrotliMagic);
                            break;
                        case WebGLCompressionTypes.GZip:
                            AnsiConsole.MarkupLineInterpolated(
                                $"( INFO ) Compressing [green]{webDataFile}[/] using GZip compression. Please be patient, it might take some time..."
                            );
                            GZipUtils.CompressFile(webDataFile, selectedFile);
                            break;
                        default:
                            UnityWebDataHelper.PackFilesToWebData(
                                unpackedWebDataDirectory,
                                selectedFile
                            );
                            break;
                    }

                    if (
                        webGLCompressionType == WebGLCompressionTypes.Brotli
                        || webGLCompressionType == WebGLCompressionTypes.GZip
                    )
                        File.Delete(webDataFile);
                }

                //Directory.Delete(unpackedWebDataDirectory, true);
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
                "USSR is a CLI tool to easily remove Unity splash screen logo (Made with Unity) from your game and keeping your logo displayed. USSR didn't directly \"hack\" Unity Editor, but the generated build."
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "Before using USSR, make sure you have set splash screen \"Draw Mode\" in Player Settings to \"All Sequential\" and don't forget to backup your game files (USSR by default backuping your game files before doing it\'s job, but might be not because of bugs)."
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine(
                "For more information, visit USSR GitHub repo: [link]https://github.com/kiraio-moe/USSR[/]"
            );
            Console.WriteLine();
            AnsiConsole.MarkupLine("[bold green]How to Use[/]");
            AnsiConsole.MarkupLine("Select the Action, find and choose one of these files in you game data:");
            AnsiConsole.MarkupLine("[green]globalgamemanagers[/] | [green]data.unity3d[/] | [green]<game_name>.data[/] | [green]<game_name>.data.br[/] | [green]<game_name>.data.gz[/]");
        }

        static void LoadClassPackage(AssetsManager assetsManager, string tpkFile)
        {
            try
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Loading class types package: [green]{tpkFile}[/]..."
                );

                if (File.Exists(tpkFile))
                    assetsManager.LoadClassPackage(path: tpkFile);
                else
                    AnsiConsole.MarkupLineInterpolated(
                        $"( ERROR ) TPK file not found: [green]{tpkFile}[/]..."
                    );
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[red]( ERROR )[/] Unable to load class types package! {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Load AssetFileInstance.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        static AssetsFileInstance? LoadAssetFileInstance(
            string sourceFile,
            AssetsManager assetsManager
        )
        {
            try
            {
                if (File.Exists(sourceFile))
                    return assetsManager.LoadAssetsFile(sourceFile, true);
                else
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERROR )[/] File not found: [red]{sourceFile}[/]"
                    );
                    return null;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                return null;
            }
        }

        /// <summary>
        /// Load AssetFileInstance from <paramref name="bundleFileInstance"/>.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <param name="assetsManager"></param>
        /// <param name="bundleFileInstance"></param>
        /// <returns></returns>
        static AssetsFileInstance? LoadAssetFileInstance(
            string sourceFile,
            AssetsManager assetsManager,
            BundleFileInstance? bundleFileInstance
        )
        {
            try
            {
                if (File.Exists(sourceFile))
                    return assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, 0, true);
                else
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERROR )[/] File not found: [red]{sourceFile}[/]"
                    );
                    return null;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShowLinks);
                return null;
            }
        }

        static BundleFileInstance? LoadBundleFileInstance(
            string sourceFile,
            AssetsManager assetsManager,
            FileStream? unpackedSourceFileStream
        )
        {
            try
            {
                if (File.Exists(sourceFile))
                {
                    BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(
                        sourceFile,
                        false
                    );
                    unpackedSourceFileStream = File.Open($"{sourceFile}.unpacked", FileMode.Create);
                    bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                        bundleFileInstance.file,
                        unpackedSourceFileStream
                    );

                    return bundleFileInstance;
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERROR )[/] File not found: [red]{sourceFile}[/]"
                    );
                    return null;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                return null;
            }
        }

        static List<AssetsReplacer>? RemoveSplashScreen(
            AssetsManager assetsManager,
            AssetsFileInstance? assetFileInstance
        )
        {
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
            AssetTypeValueField playerSettingsBase = assetsManager.GetBaseField(
                assetFileInstance,
                playerSettingsInfo?[0]
            );

            // Required fields to remove splash screen
            bool hasProVersion = buildSettingsBase["hasPROVersion"].AsBool;
            bool showUnityLogo = playerSettingsBase["m_ShowUnitySplashLogo"].AsBool;

            AnsiConsole.MarkupLine("( INFO ) Removing Unity splash screen...");

            // Check if the splash screen have been removed
            if (hasProVersion && !showUnityLogo)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]( WARN ) Unity splash screen have been removed![/]"
                );
                return null;
            }

            // AnsiConsole.MarkupLine(
            //     "( INFO ) Sometimes USSR [yellow]can\'t automatically detect Unity splash screen logo[/] and it\'s leading to accidentally [red]removing your own logo[/]. To tackle this, USSR [green]need information about \"Made With Unity\" logo duration[/]."
            // );
            // AnsiConsole.MarkupLine(
            //     "( INFO ) Please [red]make a difference with the logo duration[/] when you build your game! [red]If your logo and Unity logo have same duration, USSR will remove both of them[/]. If no value provided, USSR will use it\'s own way to detect it and [red]may removing your own logo[/]."
            // );
            // AnsiConsole.Markup("[green](Optional)[/] Enter Unity splash screen logo duration: ");

            // int unityLogoDuration = 0;
            // try
            // {
            //     int.TryParse(
            //         Console.ReadLine(),
            //         System.Globalization.NumberStyles.Integer,
            //         null,
            //         out unityLogoDuration
            //     );
            // }
            // catch (Exception ex)
            // {
            //     AnsiConsole.WriteException(ex);
            // }

            AnsiConsole.MarkupLineInterpolated(
                $"( INFO ) Set [green]hasProVersion = {!hasProVersion}[/] | [green]m_ShowUnitySplashLogo = {!showUnityLogo}[/]"
            );

            // Remove Unity splash screen by flipping these boolean fields
            buildSettingsBase["hasPROVersion"].AsBool = !hasProVersion; // true
            playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false

            AssetTypeValueField splashScreenLogos = playerSettingsBase["m_SplashScreenLogos.Array"];
            AnsiConsole.MarkupLineInterpolated($"( INFO ) There's [green]{splashScreenLogos.Count()}[/] splash screen detected. What order are Unity splash screen logo in your Player Settings? (Start from 0)");
            int.TryParse(
                Console.ReadLine(),
                System.Globalization.NumberStyles.Integer,
                null,
                out int unitySplashIndex
            );

            InputLogoIndex:
            if (unitySplashIndex < 0 && unitySplashIndex > splashScreenLogos.Count())
            {
                AnsiConsole.MarkupLineInterpolated($"( ERROR ) There's no logo at index {unitySplashIndex}! Try again!");
                goto InputLogoIndex;
            }

            AnsiConsole.MarkupLineInterpolated(
                $"( INFO ) [green]Removing Unity splash screen at index {unitySplashIndex}.[/]"
            );
            splashScreenLogos?.Children.RemoveAt(unitySplashIndex);

            return new()
            {
                new AssetsReplacerFromMemory(assetFile, buildSettingsInfo?[0], buildSettingsBase),
                new AssetsReplacerFromMemory(assetFile, playerSettingsInfo?[0], playerSettingsBase)
            };
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
                buildSettingsBase["isNoWatermarkBuild"].AsBool = !noWatermark;
                buildSettingsBase["isTrial"].AsBool = !isTrial;

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
                    $"( ERROR ) Unable to remove watermark! {ex.Message}"
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
                AnsiConsole.WriteException(ex);
                return;
            }
            finally
            {
                File.Delete(uncompressedBundleFile);
            }
        }
    }
}
