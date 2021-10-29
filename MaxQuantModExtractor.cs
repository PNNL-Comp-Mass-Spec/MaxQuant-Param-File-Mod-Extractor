using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PRISM;

namespace MaxQuantParamFileModExtractor
{
    internal class MaxQuantModExtractor : EventNotifier
    {
        // Ignore Spelling: acetyl, acetylation, carbamidomethyl, plex

        public ModExtractorOptions Options { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public MaxQuantModExtractor(ModExtractorOptions options)
        {
            Options = options;
        }

        public bool ProcessFile(string inputFilePath)
        {
            const string WILDCARD_ASTERISK = "__WildCardAsterisk__";
            const string WILDCARD_QUESTION_MARK = "__WildCardQuestionMark__";

            try
            {
                var filesToProcess = new List<FileInfo>();

                if (inputFilePath.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    var cleanFilePath = inputFilePath.Replace("*", WILDCARD_ASTERISK).Replace("?", WILDCARD_QUESTION_MARK);
                    var placeholderFile = new FileInfo(cleanFilePath);

                    if (placeholderFile.Directory == null)
                    {
                        OnWarningEvent("Unable to determine the parent directory of the input file");
                        return false;
                    }

                    var fileMask = placeholderFile.Name.Replace(WILDCARD_ASTERISK, "*").Replace(WILDCARD_QUESTION_MARK, "?");
                    foreach (var matchingFile in placeholderFile.Directory.GetFiles(fileMask))
                    {
                        filesToProcess.Add(matchingFile);
                    }

                    if (filesToProcess.Count == 0)
                    {
                        OnWarningEvent("No files matching {0} were found in {1}", fileMask, placeholderFile.Directory.FullName);
                        return false;
                    }
                }
                else
                {
                    filesToProcess.Add(new FileInfo(inputFilePath));
                    if (!filesToProcess[0].Exists)
                    {
                        OnWarningEvent("File not found: {0}", inputFilePath);
                        return false;
                    }
                }

                var failedFiles = new List<FileInfo>();

                foreach (var inputFile in filesToProcess)
                {
                    var success = ExtractModNodesFromParameterFile(inputFile);

                    if (!success)
                        failedFiles.Add(inputFile);
                }

                switch (failedFiles.Count)
                {
                    case 0:
                        return true;

                    case 1:
                        OnWarningEvent("Error processing {0}", failedFiles[0]);
                        return false;
                }

                OnWarningEvent("Error processing {0} files", failedFiles.Count);

                foreach (var file in failedFiles)
                {
                    OnStatusEvent("  {0}", PathUtils.CompactPathString(file.FullName, 100));
                }

                return false;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error occurred in Processor->ProcessFile", ex);
                return false;
            }
        }

        private bool ExtractModNodesFromParameterFile(FileSystemInfo inputFile)
        {
            try
            {
                Console.WriteLine();
                OnStatusEvent("Reading: {0}", PathUtils.CompactPathString(inputFile.FullName, 100));

                using var reader = new StreamReader(new FileStream(inputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                // Note that XDocument supersedes XmlDocument and XPathDocument
                // XDocument can often be easier to use since XDocument is LINQ-based

                var doc = XDocument.Parse(reader.ReadToEnd());

                // <fixedModifications>
                //    <string>Carbamidomethyl (C)</string>
                // </fixedModifications>

                // <variableModifications>
                //    <string>Oxidation (M)</string>
                //    <string>Acetyl (Protein N-term)</string>
                // </variableModifications>

                // ReSharper disable CommentTypo

                // <isobaricLabels>
                //   <IsobaricLabelInfo>
                //     <internalLabel>TMT6plex-Lys126</internalLabel>
                //     <terminalLabel>TMT6plex-Nter126</terminalLabel>
                //     <correctionFactorM2>0</correctionFactorM2>
                //     <correctionFactorM1>0</correctionFactorM1>
                //     <correctionFactorP1>0</correctionFactorP1>
                //     <correctionFactorP2>0</correctionFactorP2>
                //     <tmtLike>True</tmtLike>
                //   </IsobaricLabelInfo>
                //   ...
                // </isobaricLabels>

                // ReSharper restore CommentTypo

                var restrictModNodes = doc.Elements("MaxQuantParams").Elements("restrictMods").Elements("string").ToList();

                var parameterGroupNodes = doc.Elements("MaxQuantParams").Elements("parameterGroups").Elements("parameterGroup").ToList();

                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (parameterGroupNodes.Count == 0)
                {
                    OnWarningEvent("MaxQuant parameter file is missing the <parameterGroup> element; cannot extract modification info");
                    return false;
                }

                if (parameterGroupNodes.Count > 1)
                {
                    OnWarningEvent("MaxQuant parameter file has more than one <parameterGroup> element; this is allowed, but not usually used");
                }

                Console.WriteLine();

                var dynamicMods = new SortedSet<string>();

                var groupNumber = 0;
                foreach (var parameterGroup in parameterGroupNodes)
                {
                    groupNumber++;
                    if (parameterGroupNodes.Count > 1)
                    {
                        Console.WriteLine("Parameter group {0}", groupNumber);
                    }

                    var firstSearchDynamicModNodes = parameterGroup.Elements("variableModificationsFirstSearch").Elements("string").ToList();

                    var fixedModNodes = parameterGroup.Elements("fixedModifications").Elements("string").ToList();

                    var dynamicModNodes = parameterGroup.Elements("variableModifications").Elements("string").ToList();

                    // Check for isobaric mods, e.g. 6-plex or 10-plex TMT
                    var internalIsobaricLabelNodes = parameterGroup.Elements("isobaricLabels").Elements("IsobaricLabelInfo").Elements("internalLabel").ToList();

                    var terminalIsobaricLabelNodes = parameterGroup.Elements("isobaricLabels").Elements("IsobaricLabelInfo").Elements("terminalLabel").ToList();

                    if (fixedModNodes.Count > 0)
                    {
                        Console.WriteLine("    <fixedModifications>");
                        foreach (var fixedMod in fixedModNodes)
                        {
                            Console.WriteLine("        <string>{0}</string>", fixedMod.Value);
                        }
                        Console.WriteLine("    </fixedModifications>");
                    }

                    if (dynamicModNodes.Count > 0 || firstSearchDynamicModNodes.Count > 0)
                    {
                        Console.WriteLine("    <variableModifications>");

                        foreach (var modName in dynamicModNodes.Select(dynamicMod => dynamicMod.Value))
                        {
                            Console.WriteLine("        <string>{0}</string>", modName);

                            if (!dynamicMods.Contains(modName))
                            {
                                dynamicMods.Add(modName);
                            }
                        }

                        foreach (var modName in firstSearchDynamicModNodes.Select(dynamicMod => dynamicMod.Value))
                        {
                            if (dynamicMods.Contains(modName))
                                continue;

                            Console.WriteLine("        <string>{0}</string>", modName);
                        }

                        Console.WriteLine("    </variableModifications>");
                    }

                    if (internalIsobaricLabelNodes.Count > 0 || terminalIsobaricLabelNodes.Count > 0)
                    {
                        Console.WriteLine("    <isobaricLabels>");
                        Console.WriteLine("       <IsobaricLabelInfo>");

                        if (internalIsobaricLabelNodes.Count > 0)
                        {
                            Console.WriteLine("          <internalLabel>{0}</internalLabel>", internalIsobaricLabelNodes[0].Value);
                        }

                        if (terminalIsobaricLabelNodes.Count > 0)
                        {
                            Console.WriteLine("          <terminalLabel>{0}</terminalLabel>", terminalIsobaricLabelNodes[0].Value);
                        }

                        Console.WriteLine("       </IsobaricLabelInfo>");
                        Console.WriteLine("    </isobaricLabels>");
                    }
                }

                Console.WriteLine();

                // Check whether any mods in the <restrictMods> section are other than oxidation or N-terminal acetylation
                foreach (var modName in restrictModNodes.Select(dynamicMod => dynamicMod.Value))
                {
                    if (modName.Equals("Oxidation (M)") || modName.Equals("Acetyl (Protein N-term)"))
                        continue;

                    ConsoleMsgUtils.ShowWarning(
                        "Dynamic mod {0} is defined in the <restrictMods> section, which means it will be considered during protein quantification;\n" +
                        "  typically, only oxidized methionine and N-terminal acetylation should be defined here",
                        modName);
                }

                // Look for mods in the <restrictMods> section that are not in a dynamic mod defined for the main search
                foreach (var modName in restrictModNodes.Select(dynamicMod => dynamicMod.Value))
                {
                    if (!dynamicMods.Contains(modName))
                    {
                        ConsoleMsgUtils.ShowWarning(
                            "Dynamic mod {0} is defined in the <restrictMods> section, but is not defined in the <variableModifications> section;\n" +
                            "  this is likely an error",
                            modName);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error occurred in ExtractModNodesFromParameterFile", ex);
                return false;
            }
        }
    }
}
