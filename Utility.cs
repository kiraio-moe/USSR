namespace USSR.Utilities
{
    public class Utility
    {
        /// <summary>
        /// Clone a file.
        /// </summary>
        /// <param name="sourceFilePath"></param>
        /// <param name="backupDestinationPath"></param>
        /// <returns>Cloned file path</returns>
        public static string CloneFile(string sourceFilePath, string backupDestinationPath)
        {
            try
            {
                // Check if the source file exists
                if (!File.Exists(sourceFilePath))
                {
                    throw new FileNotFoundException("Backup source file doesn\'t exist!");
                }

                // Create the backup destination directory if it doesn't exist
                string? backupDir = Path.GetDirectoryName(backupDestinationPath);
                if (Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                // Copy the source file to the backup destination
                File.Copy(sourceFilePath, backupDestinationPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during the backup process: {ex.Message}");
            }

            return backupDestinationPath;
        }

        /// <summary>
        /// Backup a file. If it's already exist, skip.
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static string BackupOnlyOnce(string? sourceFile)
        {
            string backupFile = $"{sourceFile}.bak";

            if (!File.Exists(backupFile))
            {
                Console.WriteLine("Backup original file...");

                CloneFile(sourceFile, backupFile);
            }

            return backupFile;
        }

        /// <summary>
        /// Delete <paramref name="files"/>.
        /// </summary>
        /// <param name="files"></param>
        public static void CleanUp(List<string>? files)
        {
            if (files?.Count < 1)
                return;

            foreach (string file in files)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }

        /// <summary>
        /// Check if File exists.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static bool CheckFile(string? file)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"{file} didn\'t exist! The file is moved or deleted.");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Find an asset file.
        /// </summary>
        /// <param name="assetFileList"></param>
        /// <param name="rootDirectory"></param>
        /// <returns>If one is found, return the path. Otherwise null.</returns>
        public static string? FindAsset(
            string[] assetFileList,
            string rootDirectory,
            string? filePrefix = null,
            string? filePostfix = null
        )
        {
            string? assetFile = null;

            foreach (string file in assetFileList)
            {
                assetFile = Path.Combine(rootDirectory, "${filePrefix}{file}{filePostfix}");

                if (File.Exists(assetFile))
                    break;
            }

            return assetFile;
        }
    }
}
