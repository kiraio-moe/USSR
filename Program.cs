using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using NativeFileDialogSharp;
using Spectre.Console;
using USSR.Utilities;

//using System.Linq;

namespace USSR
{
    public class Program
    {
        const string VERSION = "1.2.4";
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
        static readonly byte[] unityBrotliMagic = { 0x6B, 0x8D, 0x00, 0x55, 0x6E, 0x69, 0x74, 0x79, 0x57, 0x65, 0x62, 0x20, 0x43, 0x6F, 0x6D, 0x70, 0x72, 0x65, 0x73, 0x73, 0x65, 0x64, 0x20, 0x43, 0x6F, 0x6E, 0x74, 0x65, 0x6E, 0x74, 0x20, 0x28, 0x62, 0x72, 0x6F, 0x74, 0x6C, 0x69, 0x29 };

        enum AssetTypes
        {
            Asset,
            Bundle,
            WebData
        }
        static AssetTypes assetType;

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

            string? selectedFile = string.Empty, originalFileName = string.Empty;
            string? webDataFile = string.Empty, unpackedWebDataDirectory = string.Empty;

            switch (choiceIndex)
            {
                case 0:
                case 1:
                    // Unfortunately, only one filter are supported.
                    // Instead of working around with this, we just need to manually validate by reading the file header later.
                    DialogResult filePicker = Dialog.FileOpen(null, ussrExec);

                    if (filePicker.IsCancelled)
                        goto ChooseAction;
                    else if (filePicker.IsError)
                    {
                        AnsiConsole.MarkupLine("[red]( ERROR )[/]Unable to open File Picker dialog!");
                        goto ChooseAction;
                    }

                    selectedFile = originalFileName = filePicker.Path;
                    break;
                case 2:
                    AnsiConsole.MarkupLine("Have a nice day ;)");
                    Console.ReadLine();
                    return;
            }

            webDataFile = Path.Combine(Path.GetDirectoryName(selectedFile) ?? string.Empty, Path.GetFileNameWithoutExtension(selectedFile));

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
                assetType = AssetTypes.WebData;
            }
            else if (Utility.ValidateFile(selectedFile, unityBrotliMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]Unity Brotli[/] file selected.");
                assetType = AssetTypes.WebData;

                AnsiConsole.MarkupLineInterpolated($"Decompress [green]{selectedFile}[/]...");
                BrotliUtils.DecompressFile(selectedFile, webDataFile);
            }
            else if (Utility.ValidateFile(selectedFile, gzipMagic))
            {
                AnsiConsole.MarkupLine("( INFO ) [green]GZip[/] file selected.");
                assetType = AssetTypes.WebData;

                AnsiConsole.MarkupLineInterpolated($"Decompress [green]{selectedFile}[/]...");
                GZipUtils.DecompressFile(selectedFile, webDataFile);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]( ERROR )[/] Unknown/Unsupported file type!");
                goto ChooseAction;
            }

            AssetsManager assetsManager = new();
            string? tpkFile = Path.Combine(ussrExec ??= string.Empty, ASSET_CLASS_DB);
            LoadClassPackage(assetsManager, tpkFile);

            // List of files to be deleted later
            List<string> temporaryFiles = new();
            string inspectedFile = Utility.BackupOnlyOnce(selectedFile);

            if (assetType == AssetTypes.WebData)
            {
                // unpackedWebDataDirectory = UnityWebDataHelper.UnpackWebDataToFile(webDataFile);
                temporaryFiles.Add(unpackedWebDataDirectory = UnityWebDataHelper.UnpackWebDataToFile(webDataFile));

                // Find and select "data.unity3d" or "globalgamemanagers"
                selectedFile = Utility.FindRequiredAsset(unpackedWebDataDirectory);
            }

            AssetsFileInstance? assetFileInstance = null;
            BundleFileInstance? bundleFileInstance = null;
            FileStream? bundleStream = null;

            switch (assetType)
            {
                case AssetTypes.Asset:
                    assetFileInstance = LoadAssetFileInstance(inspectedFile);
                    break;
                case AssetTypes.Bundle:
                    bundleFileInstance = LoadBundleFileInstance(inspectedFile, bundleStream);
                    assetFileInstance = LoadAssetFileInstance(inspectedFile, bundleFileInstance);
                    break;
            }

            // try
            // {
            //     if (File.Exists(globalgamemanagersFile))
            //     {
            //         AnsiConsole.MarkupLine("Found [green]globalgamemanagers[/].");

            //         // Make temporary copy, so the original file ready to be overwritten
            //         string? tempFile = Utility.CloneFile(
            //             globalgamemanagersFile,
            //             $"{globalgamemanagersFile}.temp"
            //         );
            //         temporaryFiles.Add(tempFile);

            //         AnsiConsole.MarkupLine("Loading [green]globalgamemanagers[/] and it's dependencies...");
            //         assetFileInstance = assetsManager.LoadAssetsFile(tempFile, true);
            //     }

            //     if (File.Exists(unity3dFile))
            //     {
            //         AnsiConsole.MarkupLine("Found [green]data.unity3d[/].");

            //         string? tempFile = Utility.CloneFile(
            //             unity3dFile,
            //             $"{unity3dFile}.temp"
            //         );
            //         temporaryFiles.Add(tempFile);

            //         AnsiConsole.MarkupLine("Unpacking [green]data.unity3d[/] file...");
            //         string? unpackedBundleFile = $"{unity3dFile}.unpacked";
            //         temporaryFiles.Add(unpackedBundleFile);

            //         bundleStream = File.Open(unpackedBundleFile, FileMode.Create);
            //         bundleFileInstance = assetsManager.LoadBundleFile(tempFile, false);
            //         bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
            //             bundleFileInstance.file,
            //             bundleStream
            //         );

            //         AnsiConsole.MarkupLine("Loading [green]globalgamemanagers[/] and it's dependencies...");
            //         assetFileInstance = assetsManager.LoadAssetsFileFromBundle(
            //             bundleFileInstance,
            //             0,
            //             true
            //         );
            //     }
            // }
            // catch (Exception ex)
            // {
            //     AnsiConsole.MarkupLine($"Error loading asset file. {ex.Message}");
            //     Console.ReadLine();
            //     return;
            // }

            // AssetBundleFile? bundleFile = bundleFileInstance?.file;
            // AssetsFile? assetFile = assetFileInstance?.file;

            AnsiConsole.MarkupLine("( INFO ) Loading asset class types database...");
            assetsManager.LoadClassDatabaseFromPackage(assetFileInstance?.file.Metadata.UnityVersion);

            // List<AssetFileInfo>? buildSettingsInfo = assetFile?.GetAssetsOfType(
            //     AssetClassID.BuildSettings
            // );
            // // Get BuildSettings base field
            // AssetTypeValueField? buildSettingsBase = assetsManager.GetBaseField(
            //     assetFileInstance,
            //     buildSettingsInfo?[0]
            // );

            // List<AssetFileInfo>? playerSettingsInfo = assetFile?.GetAssetsOfType(
            //     AssetClassID.PlayerSettings
            // );
            // // Get PlayerSettings base field
            // AssetTypeValueField? playerSettingsBase = assetsManager.GetBaseField(
            //     assetFileInstance,
            //     playerSettingsInfo?[0]
            // );
            // Get m_SplashScreenLogos field as array
            // AssetTypeValueField? splashScreenLogos = playerSettingsBase[
            //     "m_SplashScreenLogos.Array"
            // ];

            // Get required fields to remove the splash screen
            // bool isProVersion = buildSettingsBase["hasPROVersion"].AsBool;
            // bool showUnityLogo = playerSettingsBase["m_ShowUnitySplashLogo"].AsBool;
            // bool noWatermark = buildSettingsBase["isNoWatermarkBuild"].AsBool;

            // if (isProVersion && !showUnityLogo)
            // {
            //     AnsiConsole.MarkupLine(
            //         "[yellow]Unity splash screen logo didn't exist or already removed. Nothing to do.[/]"
            //     );

            //     assetsManager.UnloadAll(true);
            //     Utility.CleanUp(temporaryFiles);

            //     // if (isWebGL)
            //     // {
            //     //     Directory.Delete(gameDataDirectory, true);
            //     //     if (webGLCompressionType != ".data")
            //     //         File.Delete(rawWebGLFile);
            //     // }

            //     Console.ReadLine();
            //     return;
            // }

            // AnsiConsole.MarkupLine(
            //     "[yellow]Sometimes USSR can\'t automatically detect Unity splash screen logo and it\'s leading to accidentally removing your own logo.[/] To tackle this, USSR needed information about \"Made With Unity\" logo duration."
            // );
            // AnsiConsole.MarkupLine(
            //     "[red]Please make a difference with the logo duration when you build your game! If your logo and Unity logo have same duration, USSR will remove both of them.[/] If no value provided, USSR will use it\'s own way to detect it and [red]may removing your own logo[/]."
            // );
            // AnsiConsole.Markup("[green](Optional)[/] Enter Unity splash screen logo duration: ");

            // int.TryParse(
            //     Console.ReadLine(),
            //     System.Globalization.NumberStyles.Integer,
            //     null,
            //     out int logoDuration
            // );

            // AnsiConsole.MarkupLine("Removing Unity splash screen...");

            // Remove Unity splash screen by flipping these boolean fields
            // buildSettingsBase["hasPROVersion"].AsBool = !isProVersion; // true
            // playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false
            // buildSettingsBase["isNoWatermarkBuild"].AsBool = !noWatermark;

            // Iterate over "m_SplashScreenLogos" to find Unity splash screen logo
            // AssetTypeValueField? unityLogo = null;

            // foreach (AssetTypeValueField data in splashScreenLogos)
            // {
            //     // Get the Sprite asset
            //     AssetTypeValueField? logoPointer = data?["logo"];
            //     // Get the external asset
            //     AssetExternal logoExtInfo = assetsManager.GetExtAsset(
            //         assetFileInstance,
            //         logoPointer
            //     );

            //     if (logoExtInfo.baseField != null)
            //     {
            //         // Get the base field
            //         AssetTypeValueField? logoBase = logoExtInfo.baseField;
            //         string? logoName = logoBase["m_Name"].AsString;

            //         // If it's Unity splash screen logo
            //         if (logoName.Contains("UnitySplash-cube"))
            //             unityLogo = data;
            //     }
            //     else
            //     {
            //         /*
            //         * IDK why AssetsTools won't load "UnitySplash-cube"
            //         * external asset while in Bundle file. So, we can
            //         * check it's name and remove it like before.
            //         *
            //         * Alternatively, we can still find it by using
            //         * logo duration or checking if the base field is null.
            //         */
            //         if (data?["duration"].AsInt == logoDuration)
            //             unityLogo = data;
            //         else
            //             unityLogo = data;
            //     }
            // }

            // /*
            // * Remove "UnitySplash-cube" to completely remove
            // * Unity splash screen logo. So, Only our logo remained.
            // */
            // if (unityLogo != null)
            //     splashScreenLogos?.Children.Remove(unityLogo);

            // AnsiConsole.MarkupLine("[green]Done.[/]");

            // // Store modified base fields
            // List<AssetsReplacer>? assetsReplacers =
            //     new()
            //     {
            //         new AssetsReplacerFromMemory(
            //             assetFile,
            //             buildSettingsInfo?[0],
            //             buildSettingsBase
            //         ),
            //         new AssetsReplacerFromMemory(
            //             assetFile,
            //             playerSettingsInfo?[0],
            //             playerSettingsBase
            //         )
            //     };

            List<AssetsReplacer>? assetsReplacer = new();

            switch (choiceIndex)
            {
                case 0:
                    assetsReplacer = RemoveSplashScreen(assetsManager, assetFileInstance);
                    break;
                case 1:
                    assetsReplacer = RemoveWatermark(assetsManager, assetFileInstance);
                    break;
            }

            WriteChanges(selectedFile, assetFileInstance, bundleFileInstance, assetsReplacer);
            assetsManager.UnloadAll(true);
            Utility.CleanUp(temporaryFiles);

            // try
            // {
            //     // Write modified asset file to disk
            //     AnsiConsole.MarkupLine("Writing changes to disk...");

            //     if (File.Exists(globalgamemanagersFile))
            //     {
            //         Utility.BackupOnlyOnce(globalgamemanagersFile);

            //         using AssetsFileWriter writer = new(globalgamemanagersFile);
            //         assetFile?.Write(writer, 0, assetsReplacers);
            //     }

            //     if (File.Exists(unity3dFile))
            //     {
            //         Utility.BackupOnlyOnce(unity3dFile);

            //         List<BundleReplacer> bundleReplacers =
            //             new()
            //             {
            //                 new BundleReplacerFromAssets(
            //                     assetFileInstance?.name,
            //                     null,
            //                     assetFile,
            //                     assetsReplacers
            //                 )
            //             };

            //         string uncompressedBundleFile = $"{unity3dFile}.uncompressed";
            //         temporaryFiles.Add(uncompressedBundleFile);

                    // Write modified assets to uncompressed asset bundle
                //     using (AssetsFileWriter writer = new(uncompressedBundleFile))
                //         bundleFile?.Write(writer, bundleReplacers);

                //     // Compress asset bundle
                //     using (
                //         FileStream? uncompressedBundleStream = File.OpenRead(uncompressedBundleFile)
                //     )
                //     {
                //         AnsiConsole.MarkupLine("Compressing [green]data.unity3d[/] file...");

                //         AssetBundleFile? uncompressedBundle = new();
                //         uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                //         using AssetsFileWriter writer = new(unity3dFile);
                //         uncompressedBundle.Pack(
                //             uncompressedBundle.Reader,
                //             writer,
                //             AssetBundleCompressionType.LZ4
                //         );
                //     }
                // }
            // }
            // catch (Exception ex)
            // {
            //     AnsiConsole.MarkupLine(
            //         $"[red]Failed to save file![/] {ex.Message} Make sure to close any processes that use it."
            //     );
            //     Console.ReadLine();
            //     return;
            // }

            // // Cleanup temporary files
            // AnsiConsole.MarkupLine("Cleaning up temporary files...");
            // bundleStream?.Close();
            // assetsManager.UnloadAllBundleFiles();
            // assetsManager.UnloadAllAssetsFiles(true);
            // Utility.CleanUp(temporaryFiles);

            // if (isWebGL)
            // {
            //     AnsiConsole.MarkupLine("Packing [green]WebGL[/] folder as [green]WebGL.data[/]...");

            //     // string? webGLdataPath = Path.Combine(execDirectory, "Build", "WebGL.data");
            //     UnityWebDataHelper.PackFilesToWebData(unpackedWebGLDirectory, rawWebGLFile);

            //     // Delete WebGL folder
            //     Directory.Delete(unpackedWebGLDirectory, true);

            //     // Compress WebGL.data if using compression
            //     switch (webGLCompressionType)
            //     {
            //         case ".data.br":
            //             AnsiConsole.MarkupLine(
            //                 "Compressing [green]WebGL.data[/] using Brotli compression. Please be patient, it might take some time..."
            //             );
            //             BrotliUtils.CompressFile(rawWebGLFile, $"{rawWebGLFile}.br");
            //             break;
            //         case ".data.gz":
            //             AnsiConsole.MarkupLine(
            //                 "Compressing [green]WebGL.data[/] using GZip compression. Please be patient, it might take some time..."
            //             );
            //             GZipUtils.CompressFile(rawWebGLFile, $"{rawWebGLFile}.gz");
            //             break;
            //     }

            //     if (webGLCompressionType != ".data")
            //         File.Delete(rawWebGLFile);
            // }

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
            AnsiConsole.MarkupLine("[bold green]How to Use:[/]");
            AnsiConsole.MarkupLine(
                "Select the Action and choose one of this files: [green]globalgamemanagers[/] | [green]data.unity3d[/] | [green]WebGL.data[/] | [green]WebGL.data.br[/] | [green]WebGL.data.gz[/]"
            );
        }

        static void LoadClassPackage(AssetsManager assetsManager, string tpkFile)
        {
            try
            {
                AnsiConsole.MarkupLineInterpolated($"( INFO ) Loading class types package: [green]{tpkFile}[/]...");
                assetsManager.LoadClassPackage(path: tpkFile);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[red]( ERROR )[/] Class types package not found! {ex.Message}"
                );
                Console.ReadLine();
                return;
            }
        }

        /// <summary>
        /// Load AssetFileInstance.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        static AssetsFileInstance? LoadAssetFileInstance(string sourceFile)
        {
            try
            {
                if (File.Exists(sourceFile))
                {
                    AssetsManager assetsManager = new();
                    return assetsManager.LoadAssetsFile(sourceFile, true);
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"No file found: [red]{new TextPath(sourceFile)}[/]"
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

        /// <summary>
        /// Load AssetFileInstance from <paramref name="bundleFileInstance"/>.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <param name="bundleFileInstance"></param>
        /// <returns></returns>
        static AssetsFileInstance? LoadAssetFileInstance(
            string sourceFile,
            BundleFileInstance? bundleFileInstance
        )
        {
            try
            {
                if (File.Exists(sourceFile))
                {
                    AssetsManager assetsManager = new();
                    return assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, 0, true);
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"No file found: [red]{new TextPath(sourceFile)}[/]"
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
            FileStream? unpackedSourceFileStream
        )
        {
            try
            {
                if (File.Exists(sourceFile))
                {
                    AssetsManager assetsManager = new();

                    BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(
                        sourceFile,
                        false
                    );
                    bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
                        bundleFileInstance.file,
                        unpackedSourceFileStream
                    );

                    return bundleFileInstance;
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"No file found: [red]{new TextPath(sourceFile)}[/]"
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

        // static AssetsFileInstance? LoadAssetFile(AssetsManager assetsManager, string sourceFile, AssetTypes assetType, string tpkFile)
        // {
        //     LoadClassPackage(assetsManager, tpkFile);

        //     BundleFileInstance bundleFileInstance;
        //     FileStream bundleStream;

        //     try
        //     {
        //         switch (assetType)
        //         {
        //             case AssetTypes.Asset:
        //                 AnsiConsole.MarkupLineInterpolated(
        //                     $"Loading {sourceFile} asset and it\'s dependencies..."
        //                 );

        //                 return assetsManager.LoadAssetsFile(sourceFile, true);
        //             case AssetTypes.Bundle:
        //                 AnsiConsole.MarkupLineInterpolated($"Unpacking {sourceFile}...");

        //                 string unpackedTempFile = $"{sourceFile}.unpacked";
        //                 tempFiles.Add(unpackedTempFile);

        //                 bundleStream = File.Open(unpackedTempFile, FileMode.Create);
        //                 bundleFileInstance = assetsManager.LoadBundleFile(sourceFile, false);
        //                 bundleFileInstance.file = BundleHelper.UnpackBundleToStream(
        //                     bundleFileInstance.file,
        //                     bundleStream
        //                 );

        //                 AnsiConsole.MarkupLineInterpolated(
        //                     $"Loading {sourceFile} asset and it\'s dependencies..."
        //                 );
        //                 return assetsManager.LoadAssetsFileFromBundle(
        //                     bundleFileInstance,
        //                     0,
        //                     true
        //                 );
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         AnsiConsole.WriteException(ex);
        //         return null;
        //     }
        // }

        static List<AssetsReplacer> RemoveSplashScreen(
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

            // Check if the splash screen have been removed
            if (hasProVersion && !showUnityLogo)
            {
                AnsiConsole.MarkupLine("[yellow]Unity splash screen have been removed![/]");
                goto Dispose;
            }

            AnsiConsole.MarkupLine(
                "( INFO ) Sometimes USSR [yellow]can\'t automatically detect Unity splash screen logo[/] and it\'s leading to accidentally [red]removing your own logo[/]. To tackle this, USSR [green]need information about \"Made With Unity\" logo duration[/]."
            );
            AnsiConsole.MarkupLine(
                "( INFO ) Please [red]make a difference with the logo duration[/] when you build your game! [red]If your logo and Unity logo have same duration, USSR will remove both of them[/]. If no value provided, USSR will use it\'s own way to detect it and [red]may removing your own logo[/]."
            );
            AnsiConsole.Markup("[green](Optional)[/] Enter Unity splash screen logo duration: ");

            int unityLogoDuration = 0;
            try
            {
                int.TryParse(
                    Console.ReadLine(),
                    System.Globalization.NumberStyles.Integer,
                    null,
                    out unityLogoDuration
                );
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }

            AnsiConsole.MarkupLine("( INFO ) Removing Unity splash screen...");

            AnsiConsole.MarkupLineInterpolated($"( INFO ) hasProVersion = [green]{!hasProVersion}[/] | m_ShowUnitySplashLogo = [green]{!showUnityLogo}[/]");

            // Remove Unity splash screen by flipping these boolean fields
            buildSettingsBase["hasPROVersion"].AsBool = !hasProVersion; // true
            playerSettingsBase["m_ShowUnitySplashLogo"].AsBool = !showUnityLogo; // false

            AssetTypeValueField splashScreenLogos = playerSettingsBase["m_SplashScreenLogos.Array"];
            AssetTypeValueField? unityLogo = null;

            // Iterate over "m_SplashScreenLogos" to find Unity splash screen logo
            foreach (AssetTypeValueField splashLogo in splashScreenLogos)
            {
                // Get the Sprite asset
                AssetTypeValueField? logoPointer = splashLogo?["logo"];
                // Get the external asset
                AssetExternal logoExtInfo = assetsManager.GetExtAsset(
                    assetFileInstance,
                    logoPointer
                );

                /*
                * We have 2 ways to detect the Unity splash screen logo.
                * 1. Check if the base field isn't null. This method guaranteed to be 100% work.
                * 2. Use optional input value from user to determine the splash screen.
                */

                if (logoExtInfo.baseField != null)
                {
                    // Get the base field
                    AssetTypeValueField? logoBase = logoExtInfo.baseField;
                    string? logoName = logoBase["m_Name"].AsString;

                    // If it's Unity splash screen logo
                    unityLogo = logoName.Contains("UnitySplash-cube") ? splashLogo : null;
                }
                else
                {
                    /*
                    * Sometimes AssetsTools won't load "UnitySplash-cube"
                    * external asset while in Bundle file.
                    *
                    * Luckily, we can still find it by using the logo duration.
                    */
                    unityLogo =
                        splashLogo?["duration"].AsInt == unityLogoDuration ? splashLogo : null;
                }
            }

            if (unityLogo == null && unityLogoDuration <= 0)
            {
                AnsiConsole.MarkupLine(
                    "[red]( ERROR )[/] Failed to remove Unity splash screen logo!"
                );
                AnsiConsole.MarkupLine(
                    "Looks like USSR [red]can'\t detect the Unity splash screen[/] and at the same time [red]you didn'\t provide any value to the input[/] to help USSR find the splash screen. [yellow]Try again and fill in the input.[/]"
                );
                goto Dispose;
            }

            /*
            * Remove "UnitySplash-cube" to completely remove
            * Unity splash screen logo.
            */
            AnsiConsole.MarkupLineInterpolated($"( INFO ) Removing [red]UnitySplash-cube[/]...");
            splashScreenLogos?.Children.Remove(unityLogo);

            AnsiConsole.MarkupLine("( INFO ) [green]Successfully removed the Unity splash screen.[/]");

            return new()
            {
                new AssetsReplacerFromMemory(assetFile, buildSettingsInfo?[0], buildSettingsBase),
                new AssetsReplacerFromMemory(assetFile, playerSettingsInfo?[0], playerSettingsBase)
            };

            Dispose:
            assetsManager.UnloadAll(true);
            return new();
        }

        static List<AssetsReplacer>? RemoveWatermark(AssetsManager assetsManager, AssetsFileInstance? assetFileInstance)
        {
            try
            {
                AnsiConsole.MarkupLine("( INFO ) Removing watermark...");

                AssetsFile? assetFile = assetFileInstance?.file;
                List<AssetFileInfo>? buildSettingsInfo = assetFile?.GetAssetsOfType(AssetClassID.BuildSettings);
                AssetTypeValueField buildSettingsBase = assetsManager.GetBaseField(assetFileInstance, buildSettingsInfo?[0]);

                AnsiConsole.MarkupLineInterpolated($"( INFO ) isNoWatermarkBuild = [green]true[/]");
                buildSettingsBase["isNoWatermarkBuild"].AsBool = true;

                AnsiConsole.MarkupLine("( INFO ) [green]Watermark successfully removed.[/]");
                return new()
                {
                    new AssetsReplacerFromMemory(assetFile, buildSettingsInfo?[0], buildSettingsBase)
                };
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"( ERROR ) Unable to remove watermark! {ex.Message}");
                return null;
            }
        }

        static void WriteChanges(string modifiedFile, AssetsFileInstance? assetFileInstance, BundleFileInstance? bundleFileInstance, List<AssetsReplacer> assetsReplacer)
        {
            string uncompressedBundleFile = $"{modifiedFile}.uncompressed";

            try
            {
                switch (assetType)
                {
                    case AssetTypes.Asset:
                    {
                        AnsiConsole.MarkupLineInterpolated($"( INFO ) Writing changes to [green]{modifiedFile}[/]...");
                        using AssetsFileWriter writer = new(modifiedFile);
                        assetFileInstance?.file.Write(writer, 0, assetsReplacer);
                        break;
                    }
                    case AssetTypes.Bundle:
                    {
                        List<BundleReplacer> bundleReplacer = new()
                        {
                            new BundleReplacerFromAssets(
                                assetFileInstance?.name,
                                null,
                                assetFileInstance?.file,
                                assetsReplacer
                            )
                        };

                        using AssetsFileWriter writer = new(uncompressedBundleFile);
                        bundleFileInstance?.file.Write(writer, bundleReplacer);

                        using FileStream uncompressedBundleStream = File.OpenRead(uncompressedBundleFile);
                        AssetBundleFile uncompressedBundle = new();
                        uncompressedBundle.Read(new AssetsFileReader(uncompressedBundleStream));

                        using AssetsFileWriter uncompressedWriter = new(modifiedFile);
                        uncompressedBundle.Pack(uncompressedBundle.Reader, uncompressedWriter, AssetBundleCompressionType.LZ4);
                        break;
                    }
                    case AssetTypes.WebData:
                        break;
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
