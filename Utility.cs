using Spectre.Console;

namespace USSR.Utilities
{
    internal class Utility
    {
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
                        AnsiConsole.MarkupLine("[red]Unknown/Unsupported[/] file type!");
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
                    AnsiConsole.WriteLine("[red]Source file doesn\'t exist![/]");
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
        /// Backup a file. If it's already exist, skip.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        internal static string BackupOnlyOnce(string sourceFile)
        {
            string backupFile = $"{sourceFile}.bak";

            if (!File.Exists(backupFile))
            {
                AnsiConsole.MarkupLine("Backup original file...");

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
            if (paths?.Count < 1)
                return;

            foreach (string path in paths)
            {
                if (File.Exists(path))
                    File.Delete(path);

                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        /// <summary>
        /// Check if File exists. Return default message if not.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        internal static bool CheckFile(string? file)
        {
            if (!File.Exists(file))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{file} didn\'t exist![/] The file is moved or deleted.");
                return false;
            }
            return true;
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
                if (File.Exists(path = Path.Combine(directoryPath, asset))) break;
            }

            return path;
        }
    }
}
