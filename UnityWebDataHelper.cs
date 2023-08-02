using Kaitai;

namespace USSR.Utilities
{
    public class UnityWebDataHelper
    {
        /// <summary>
        /// Unpack UnityWebData (WebGL.data) to File.
        /// </summary>
        /// <param name="bundleFile"></param>
        /// <returns>Output directory.</returns>
        public static string UnpackBundleToFile(string? bundleFile)
        {
            if (!File.Exists(bundleFile))
                throw new FileNotFoundException($"{bundleFile} didn\'t exist!");

            // Create the Kaitai stream and the root object from the parsed data
            UnityWebData? unityWebData = UnityWebData.FromFile(bundleFile);

            string? outputDirectory = Path.Combine(
                Path.GetDirectoryName(bundleFile),
                Path.GetFileNameWithoutExtension(bundleFile)
            );
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Console.WriteLine("Extracting bundle file...");

            foreach (UnityWebData.FileEntry fileEntry in unityWebData.Files)
            {
                string? fileName = fileEntry?.Filename;

                // Create file entry directory
                string? fileNameDirectory = Path.Combine(
                    outputDirectory,
                    Path.GetDirectoryName(fileName)
                );
                if (!Directory.Exists(fileNameDirectory))
                    Directory.CreateDirectory(fileNameDirectory);

                string? outputFile = Path.Combine(outputDirectory, fileName);

                using FileStream? outputFileStream = new(outputFile, FileMode.Create);
                outputFileStream?.Write(fileEntry?.Data);
            }

            Console.WriteLine("Extraction complete.");
            return outputDirectory;
        }

        // TODO: Pack files to WebGL.data
        public static void PackFilesToBundle(string? sourceDirectory) { }
    }
}
