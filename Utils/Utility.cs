using Spectre.Console;

namespace USSR.Utilities
{
    internal class Utility
    {
        const string LAST_OPEN_FILE = "last_open.txt";

        /// <summary>
        /// Check the file signature if it's a valid file.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="fileSignature"></param>
        /// <returns>The file are valid or not.</returns>
        internal static bool ValidateFile(string filePath, byte[] fileSignature)
        {
            try
            {
                byte[] sourceFileSignature = new byte[fileSignature.Length];

                using FileStream file = File.OpenRead(filePath);
                file.Read(sourceFileSignature, 0, fileSignature.Length);

                for (int i = 0; i < fileSignature.Length; i++)
                {
                    if (sourceFileSignature[i] != fileSignature[i])
                    {
                        // AnsiConsole.MarkupLine("[red]Unknown/Unsupported[/] file type!");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                return false;
            }
        }

        /// <summary>
        /// Clone a file.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <param name="outputFile"></param>
        /// <returns>Cloned file path.</returns>
        internal static string CloneFile(string sourceFile, string outputFile)
        {
            try
            {
                if (!File.Exists(sourceFile))
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[red]( ERROR )[/] Source file to duplicate doesn\'t exist: [red]{sourceFile}[/]"
                    );
                    return string.Empty;
                }

                File.Copy(sourceFile, outputFile, true);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                return string.Empty;
            }

            return outputFile;
        }

        /// <summary>
        /// Backup a file as ".bak". If it's already exist, skip.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        internal static string BackupOnlyOnce(string sourceFile)
        {
            string backupFile = $"{sourceFile}.bak";

            if (!File.Exists(backupFile))
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"( INFO ) Backup [green]{Path.GetFileName(sourceFile)}[/] as [green]{backupFile}[/]..."
                );
                CloneFile(sourceFile, backupFile);
            }

            return backupFile;
        }

        /// <summary>
        /// Delete <paramref name="paths"/>.
        /// </summary>
        /// <param name="paths"></param>
        internal static void CleanUp(List<string> paths)
        {
            if (paths != null && paths?.Count > 0)
            {
                AnsiConsole.MarkupLine("( INFO ) Cleaning up temporary files...");
                foreach (string path in paths)
                {
                    if (File.Exists(path))
                        File.Delete(path);

                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// Find the required asset to remove the splash screen or watermark.
        /// </summary>
        /// <param name="directoryPath"></param>
        /// <returns>"data.unity3d" or "globalgamemanagers" file.</returns>
        internal static string FindRequiredAsset(string directoryPath)
        {
            string[] assets = { "data.unity3d", "globalgamemanagers" };
            string path = string.Empty;

            foreach (string asset in assets)
            {
                if (File.Exists(path = Path.Combine(directoryPath, asset)))
                    break;
            }

            return path;
        }

        internal static void SaveLastOpenedFile(string filePath)
        {
            try
            {
                // Write the last opened directory to a text file
                using StreamWriter writer = new(LAST_OPEN_FILE);
                writer.WriteLine($"last_opened={filePath}");
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }
        }

        internal static string GetLastOpenedFile()
        {
            string lastOpenedDirectory = string.Empty;

            if (!File.Exists(LAST_OPEN_FILE))
                return lastOpenedDirectory;

            try
            {
                // Read the last opened directory from the text file
                using StreamReader reader = new(LAST_OPEN_FILE);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("last_opened="))
                    {
                        lastOpenedDirectory = line["last_opened=".Length..];
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }

            return lastOpenedDirectory;
        }
    }
}
