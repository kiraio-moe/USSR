namespace Kiraio.USSR
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
                    return new FileNotFoundException(
                        "Backup source file does not exist!"
                    ).ToString();
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
        /// Delete unnecessary <paramref name="files"/>.
        /// </summary>
        /// <param name="files"></param>
        public static void CleanUp(List<string>? files)
        {
            if (files.Count < 1)
                return;

            foreach (string file in files)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
