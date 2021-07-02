using PRISM;

namespace MaxQuantParamFileModExtractor
{
    internal class ModExtractorOptions
    {
        // Ignore Spelling: Wildcards

        /// <summary>
        /// Input file path
        /// </summary>
        /// <remarks>.xml file</remarks>
        [Option("InputFilePath", "I",
            ArgPosition = 1, Required = true, HelpShowsDefault = false,
            HelpText = "The name of the MaxQuant parameter file to examine (XML-based parameter file). Wildcards are supported")]
        public string InputFilePath { get; set; }

        /// <summary>
        /// Validate the options
        /// </summary>
        /// <returns>True if all options are valid</returns>
        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(InputFilePath))
            {
                ConsoleMsgUtils.ShowError($"ERROR: Input path must be provided and non-empty; \"{InputFilePath}\" was provided");
                return false;
            }

            return true;
        }
    }
}
