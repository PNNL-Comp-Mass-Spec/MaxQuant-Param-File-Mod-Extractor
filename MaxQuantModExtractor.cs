using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PRISM;

namespace MaxQuantParamFileModExtractor
{
    internal class MaxQuantModExtractor : EventNotifier
    {
        // Ignore Spelling: acetyl, acetylation, carbamidomethyl, plex

        public ModExtractorOptions Options { get; set; }

        private readonly Regex mFindLeadingWhitespace = new("^[ \t]+", RegexOptions.Compiled);

        private readonly Regex mFindTagName = new("<(?<TagName>[^>]+)>", RegexOptions.Compiled);

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options">Processing options</param>
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
                        OnWarningEvent("Full path:      {0}", filesToProcess[0].FullName);
                        return false;
                    }
                }

                var failedFiles = new List<FileInfo>();

                foreach (var inputFile in filesToProcess)
                {
                    bool success;

                    if (Options.UpdateParameters)
                    {
                        success = UpdateParametersInMaxQuantParameterFile(inputFile);
                    }
                    else
                    {
                        success = ExtractModNodesFromParameterFile(inputFile);
                    }

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

        private static void AddParameterToDelete(IDictionary<string, ParameterUpdateInfo> updates, string xmlTagName)
        {
            var updateInfo = new ParameterUpdateInfo(xmlTagName, ParameterUpdateInfo.UpdateAction.Delete);

            updates.Add(xmlTagName, updateInfo);
        }

        private static void AddParameterToReplace(
            IDictionary<string, ParameterUpdateInfo> updates,
            string xmlTagName,
            IEnumerable<string> replacementParameters)
        {
            var updateInfo = new ParameterUpdateInfo(xmlTagName, ParameterUpdateInfo.UpdateAction.Replace);
            updateInfo.ParametersToAdd.AddRange(replacementParameters);

            updates.Add(xmlTagName, updateInfo);
        }

        private static void AddParameterToUpdateValue(
            IDictionary<string, ParameterUpdateInfo> updates,
            string xmlTagName,
            string newValue,
            string oldValue = "")
        {
            var updateInfo = new ParameterUpdateInfo(xmlTagName, ParameterUpdateInfo.UpdateAction.UpdateValue)
            {
                OldValue = oldValue,
                NewValue = newValue
            };

            updates.Add(xmlTagName, updateInfo);
        }

        private static void AddParametersToAppend(
            IDictionary<string, ParameterUpdateInfo> updates,
            string xmlTagName, IEnumerable<string> parametersToAppend,
            bool AppendAfterClosingTag = true)
        {
            var updateInfo = new ParameterUpdateInfo(xmlTagName, ParameterUpdateInfo.UpdateAction.AppendParameters);
            updateInfo.ParametersToAdd.AddRange(parametersToAppend);
            updateInfo.AppendAfterClosingTag = AppendAfterClosingTag;

            updates.Add(xmlTagName, updateInfo);
        }


        private void AppendParameters(StreamReader reader, TextWriter writer, string dataLine, ParameterUpdateInfo updateInfo)
        {
            var closingTag = GetClosingTag(updateInfo.TagName);
            var leadingWhitespace = GetLeadingWhitespace(dataLine);

            writer.WriteLine(dataLine);

            if (updateInfo.AppendAfterClosingTag &&!dataLine.Contains(closingTag) && !reader.EndOfStream)
            {
                var nextLine = reader.ReadLine() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(nextLine))
                {
                    writer.WriteLine(nextLine);
                }

                if (!nextLine.Contains(closingTag))
                {
                    OnWarningEvent("Closing tag not found in the current line or the next line: {0}", closingTag);
                    OnWarningEvent(dataLine);
                    OnWarningEvent(nextLine);
                }
            }

            foreach (var parameter in updateInfo.ParametersToAdd)
            {
                var additionalWhitespace = updateInfo.AppendAfterClosingTag ? string.Empty : "   ";

                writer.WriteLine("{0}{1}{2}", leadingWhitespace, additionalWhitespace, parameter.Trim());

                OnStatusEvent("Appended parameter:      <{0}>", GetTagName(parameter));
            }
        }

        private void DeleteParameter(StreamReader reader, string dataLine, ParameterUpdateInfo updateInfo)
        {
            var closingTag = GetClosingTag(updateInfo.TagName);

            if (!dataLine.Contains(closingTag) && !reader.EndOfStream)
            {
                var nextLine = reader.ReadLine() ?? string.Empty;

                if (!nextLine.Contains(closingTag))
                {
                    OnWarningEvent("Closing tag not found in the current line or the next line: {0}", closingTag);
                    OnWarningEvent("  {0}", dataLine);
                    OnWarningEvent("  {0}", nextLine);
                }
            }

            OnStatusEvent("Deleted parameter:       {0}", updateInfo.TagName);
        }

        private string GetClosingTag(string tagName)
        {
            var trimmedName = tagName.Trim();

            if (trimmedName.StartsWith("</"))
            {
                // The tag is already a closing tag
                return tagName;
            }

            if (trimmedName.StartsWith("<"))
            {
                return string.Format("</{0}", trimmedName.Substring(1));
            }

            return string.Empty;
        }

        private string GetLeadingWhitespace(string dataLine)
        {
            var match = mFindLeadingWhitespace.Match(dataLine);

            return match.Success ? match.Value : string.Empty;
        }

        private string GetTagName(string parameterValue)
        {
            var match = mFindTagName.Match(parameterValue);

            return match.Success ? match.Groups["TagName"].Value : parameterValue;
        }

        private void ReplaceParameter(StreamReader reader, TextWriter writer, string dataLine, ParameterUpdateInfo updateInfo)
        {
            var closingTag = GetClosingTag(updateInfo.TagName);
            var leadingWhitespace = GetLeadingWhitespace(dataLine);

            if (!dataLine.Contains(closingTag) && !reader.EndOfStream)
            {
                var nextLine = reader.ReadLine() ?? string.Empty;

                if (!nextLine.Contains(closingTag))
                {
                    OnWarningEvent("Closing tag not found in the current line or the next line: {0}", closingTag);
                    OnWarningEvent(dataLine);
                    OnWarningEvent(nextLine);
                }
            }

            foreach (var parameter in updateInfo.ParametersToAdd)
            {
                writer.WriteLine("{0}{1}", leadingWhitespace, parameter.Trim());

                OnStatusEvent("Replaced parameter:      {0}", updateInfo.TagName);
                OnStatusEvent("                           changed to");
                OnStatusEvent("                         <{0}>", GetTagName(parameter));
            }
        }

        private void UpdateParameterValue(StreamReader reader, TextWriter writer, string dataLine, ParameterUpdateInfo updateInfo)
        {
            var closingTag = GetClosingTag(updateInfo.TagName);
            var leadingWhitespace = GetLeadingWhitespace(dataLine);

            if (!string.IsNullOrWhiteSpace(updateInfo.OldValue) && !dataLine.Contains(updateInfo.OldValue))
            {
                OnStatusEvent("Not changing parameter value since the existing value is not '{0}': {1}", updateInfo.OldValue, dataLine.Trim());
                writer.WriteLine(dataLine);
                return;
            }

            if (!dataLine.Contains(closingTag) && !reader.EndOfStream)
            {
                var nextLine = reader.ReadLine() ?? string.Empty;

                if (!nextLine.Contains(closingTag))
                {
                    OnWarningEvent("Closing tag not found in the current line or the next line: {0}", closingTag);
                    OnWarningEvent(dataLine);
                    OnWarningEvent(nextLine);
                }
            }

            var tagName = GetTagName(updateInfo.TagName);

            var updatedLine = string.Format("{0}<{1}>{2}</{1}>", leadingWhitespace, tagName, updateInfo.NewValue);

            writer.WriteLine(updatedLine);

            OnStatusEvent("Updated parameter value: {0}", updatedLine.Trim());
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

                            // Add the modification, if not yet present
                            dynamicMods.Add(modName);
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

        private Dictionary<string, ParameterUpdateInfo> GetParameterFileUpdateInfo()
        {
            var updates = new Dictionary<string, ParameterUpdateInfo>();

            // ReSharper disable StringLiteralTypo

            AddParametersToAppend(updates, "<deNovoUseA2Score>", new List<string>
            {
                "<deNovoMassClusterTolDa>0</deNovoMassClusterTolDa>", "<deNovoScalingFactor>0</deNovoScalingFactor>"
            });

            AddParametersToAppend(updates, "<writeMzTab>", new List<string>
            {
                "<writeSdrf>False</writeSdrf>"
            });

            AddParametersToAppend(updates, "<proteinGroupingFile>", new List<string>
            {
                "<useAndromeda20>False</useAndromeda20>", "<useAndromeda20DefaultModel>False</useAndromeda20DefaultModel>", "<andromeda20AltModelPath></andromeda20AltModelPath>", "<intensityPredictionFolder></intensityPredictionFolder>"
            });

            AddParametersToAppend(
                updates,
                "<parameterGroup>",
                new List<string> { "<andromeda20AltModelPath></andromeda20AltModelPath>", "<andromeda20DefaultModel>False</andromeda20DefaultModel>", "<useAndromeda20>False</useAndromeda20>" },
                false);

            AddParametersToAppend(updates, "<lfqMinRatioCount>", new List<string>
            {
                "<lfqMinRatioCountDia>2</lfqMinRatioCountDia>", "<lfqPrioritizeMs1Dia>True</lfqPrioritizeMs1Dia>"
            });

            AddParametersToAppend(updates, "</diaMsmsPaths>", new List<string>
            {
                "<diaLabelIndsForLibraryMatch>", "</diaLabelIndsForLibraryMatch>"
            });

            AddParametersToAppend(updates, "<diaScoreN>", new List<string>
            {
                "<diaScoreNAdditional>0</diaScoreNAdditional>"
            });

            AddParametersToAppend(updates, "<diaTopNForQuant>", new List<string>
            {
                "<diaTopNCorrelationForQuant>0</diaTopNCorrelationForQuant>", "<diaFragmentCorrelationForQuant>0</diaFragmentCorrelationForQuant>"
            });

            AddParametersToAppend(updates, "<diaMinPrecursorScore>", new List<string>
            {
                "<diaUseProfileCorrelation>False</diaUseProfileCorrelation>"
            });

            AddParametersToAppend(updates, "<diaTransferQvalue>", new List<string>
            {
                "<diaTransferQvalueBetweenLabels>0</diaTransferQvalueBetweenLabels>", "<diaTransferQvalueBetweenFractions>0</diaTransferQvalueBetweenFractions>", "<diaTransferQvalueBetweenFaims>0</diaTransferQvalueBetweenFaims>"
            });

            AddParametersToAppend(updates, "<diaUseFragMassesForMl>", new List<string>
            {
                "<diaMaxTrainInstances>0</diaMaxTrainInstances>", "<diaMaxFragmentCharge>0</diaMaxFragmentCharge>", "<diaAdaptiveMlScoring>False</diaAdaptiveMlScoring>", "<diaDynamicScoringMaxInstances>25000</diaDynamicScoringMaxInstances>", "<diaMaxPrecursorMz>0</diaMaxPrecursorMz>",
                "<diaHardRtFilter>False</diaHardRtFilter>", "<diaConvertLibraryCharge2Fragments>False</diaConvertLibraryCharge2Fragments>", "<diaChargeNormalizationLibrary>False</diaChargeNormalizationLibrary>", "<diaChargeNormalizationSample>False</diaChargeNormalizationSample>",
                "<diaDeleteIntermediateResults>False</diaDeleteIntermediateResults>", "<diaScoreWeightScanIndex>0</diaScoreWeightScanIndex>", "<diaScoreWeightScanValue>0</diaScoreWeightScanValue>", "<diaNumNonleadingMatches>0</diaNumNonleadingMatches>", "<diaUseDefaultFragmentModel>True</diaUseDefaultFragmentModel>",
                "<diaAltFragmentModelPath></diaAltFragmentModelPath>", "<diaUseDefaultRtModel>True</diaUseDefaultRtModel>", "<diaAltRtModelPath></diaAltRtModelPath>", "<diaUseDefaultCcsModel>True</diaUseDefaultCcsModel>", "<diaAltCcsModelPath></diaAltCcsModelPath>", "<diaBatchProcessing>False</diaBatchProcessing>",
                "<diaBatchSize>0</diaBatchSize>", "<diaFirstBatch>0</diaFirstBatch>", "<diaLastBatch>0</diaLastBatch>", "<diaOnlyPreprocess>False</diaOnlyPreprocess>", "<diaMultiplexQuantMethod>0</diaMultiplexQuantMethod>", "<diaOnlyPostprocess>False</diaOnlyPostprocess>", "<diaRequirePrecursor>False</diaRequirePrecursor>",
                "<diaFuturePeptides>False</diaFuturePeptides>", "<diaOverrideRtWithPrediction>False</diaOverrideRtWithPrediction>", "<diaMaxModifications>0</diaMaxModifications>", "<diaMaxPositionings>0</diaMaxPositionings>", "<diaUseProbScore>False</diaUseProbScore>", "<diaProbScoreP>0</diaProbScoreP>",
                "<diaProbScoreG>0</diaProbScoreG>", "<diaProbScoreStep>0</diaProbScoreStep>", "<isM2FragTypeOverride>False</isM2FragTypeOverride>", "<ms2FragTypeOverride>0</ms2FragTypeOverride>", "<classicLfqForSingleShots>True</classicLfqForSingleShots>", "<sequenceBasedModifier>False</sequenceBasedModifier>",
                "<diaRtFromSamplesForExport>False</diaRtFromSamplesForExport>", "<diaCcsFromSamplesForExport>False</diaCcsFromSamplesForExport>", "<diaLibraryExport>0</diaLibraryExport>", "<diaUseApexWeightsForPtmLoc>False</diaUseApexWeightsForPtmLoc>", "<diaSecondScoreForMultiplex>False</diaSecondScoreForMultiplex>"
            });

            AddParametersToAppend(updates, "<IncludeAmmonia>", new List<string>
            {
                "<IncludeWaterCross>False</IncludeWaterCross>", "<IncludeAmmoniaCross>False</IncludeAmmoniaCross>"
            });

            AddParameterToDelete(updates, "<boxCarMode>");
            AddParameterToDelete(updates, "<writePeptidesForSpectrumFile>");
            AddParameterToDelete(updates, "<intensityPredictionsFile>");
            AddParameterToDelete(updates, "</intensityPredictionsFile>");
            AddParameterToDelete(updates, "<intensPred>");
            AddParameterToDelete(updates, "<intensPredModelReTrain>");
            AddParameterToDelete(updates, "<timsRearrangeSpectra>");
            AddParameterToDelete(updates, "<diaPeptidePaths>");
            AddParameterToDelete(updates, "</diaPeptidePaths>");
            AddParameterToDelete(updates, "<diaPrecursorFilterType>");
            AddParameterToDelete(updates, "<diaRtPrediction>");
            AddParameterToDelete(updates, "<diaRtPredictionSecondRound>");
            AddParameterToDelete(updates, "<ConnectedScore0>");
            AddParameterToDelete(updates, "<ConnectedScore1>");
            AddParameterToDelete(updates, "<ConnectedScore2>");

            AddParameterToReplace(updates, "<intensityThresholdMs1>", new List<string>
            {
                "<intensityThresholdMs1Dda>0</intensityThresholdMs1Dda>", "<intensityThresholdMs1Dia>0</intensityThresholdMs1Dia>"
            });

            AddParameterToReplace(updates, "<diaMinProfileCorrelation>", new List<string>
            {
                "<diaMinPrecProfileCorrelation>0</diaMinPrecProfileCorrelation>", "<diaMinFragProfileCorrelation>0</diaMinFragProfileCorrelation>"
            });
            AddParameterToReplace(updates, "<diaMinPeaksForRecal>", new List<string>
            {
                "<diaMinPeaks>5</diaMinPeaks>"
            });

            AddParameterToReplace(updates, "<Connected>", new List<string>
            {
                "<UseIntensityPrediction>False</UseIntensityPrediction>", "<UseSequenceBasedModifier>False</UseSequenceBasedModifier>"
            });

            AddParameterToReplace(updates, "<minScore_Dipeptide>", new List<string>
            {
                "<minScoreDipeptide>40</minScoreDipeptide>"
            });

            AddParameterToReplace(updates, "<minScore_Monopeptide>", new List<string>
            {
                "<minScoreMonopeptide>0</minScoreMonopeptide>"
            });

            AddParameterToReplace(updates, "<minScore_PartialCross>", new List<string>
            {
                "<minScorePartialCross>10</minScorePartialCross>"
            });

            AddParameterToReplace(updates, "<diaLfqWeightedMedian>", new List<string>
            {
                "<diaLfqRatioType>0</diaLfqRatioType>"
            });

            AddParameterToUpdateValue(updates, "<maxQuantVersion>", "2.4.13.0");
            AddParameterToUpdateValue(updates, "<fullMinMz>", "-1.79769313486232E+308");
            AddParameterToUpdateValue(updates, "<fullMaxMz>", "1.79769313486232E+308");
            AddParameterToUpdateValue(updates, "<lcmsRunType>", "Reporter MS2", "Reporter ion MS2");

            // ReSharper restore StringLiteralTypo

            return updates;
        }

        private bool UpdateParametersInMaxQuantParameterFile(FileInfo inputFile)
        {
            try
            {
                Console.WriteLine();

                if (inputFile.DirectoryName == null)
                {
                    OnErrorEvent("Unable to determine the parent directory of {0}", inputFile.FullName);
                    return false;
                }

                var outputFile = new FileInfo(Path.Combine(inputFile.DirectoryName, inputFile.Name + ".new"));

                OnStatusEvent("Updating: {0}", PathUtils.CompactPathString(inputFile.FullName, 100));

                var success = UpdateParametersWork(inputFile, outputFile);

                if (!success)
                    return false;

                var oldFile = new FileInfo(Path.Combine(inputFile.DirectoryName, inputFile.Name + ".old"));

                OnStatusEvent(string.Empty);

                if (oldFile.Exists)
                {
                    OnStatusEvent("Created {0}, but not replacing the original file since the '.old' file already exists: {1}",
                        outputFile.Name,
                        PathUtils.CompactPathString(oldFile.FullName, 120));

                    return true;
                }

                var inputFilePath = inputFile.FullName;

                inputFile.MoveTo(oldFile.FullName);

                outputFile.MoveTo(inputFilePath);

                OnStatusEvent("Updated parameters in {0}", PathUtils.CompactPathString(inputFilePath, 120));

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error occurred in UpdateParametersInMaxQuantParameterFile", ex);
                return false;
            }
        }

        private bool UpdateParametersWork(FileSystemInfo inputFile, FileSystemInfo outputFile)
        {
            try
            {
                var xmlTagMatcher = new Regex("^(?<LeadingWhitespace> *)(?<TagName><[a-z/][^> ]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var updates = GetParameterFileUpdateInfo();

                // Although an XML reader could be used, this method instead assumes the file is formatted in an expected manner and will instead use a text reader to read each line

                using var reader = new StreamReader(new FileStream(inputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                using var writer = new StreamWriter(new FileStream(outputFile.FullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));

                while (!reader.EndOfStream)
                {
                    var dataLine = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(dataLine))
                    {
                        writer.WriteLine(dataLine);
                        continue;
                    }

                    var match = xmlTagMatcher.Match(dataLine);

                    if (!match.Success)
                    {
                        writer.WriteLine(dataLine);
                        continue;
                    }

                    var tagName = string.Format("{0}>", match.Groups["TagName"].Value);

                    if (!updates.TryGetValue(tagName, out var updateInfo))
                    {
                        writer.WriteLine(dataLine);
                        continue;
                    }

                    switch (updateInfo.Action)
                    {
                        case ParameterUpdateInfo.UpdateAction.AppendParameters:
                            AppendParameters(reader, writer, dataLine, updateInfo);
                            break;

                        case ParameterUpdateInfo.UpdateAction.Delete:
                            DeleteParameter(reader, dataLine, updateInfo);
                            break;

                        case ParameterUpdateInfo.UpdateAction.Replace:
                            ReplaceParameter(reader, writer, dataLine, updateInfo);
                            break;

                        case ParameterUpdateInfo.UpdateAction.UpdateValue:
                            UpdateParameterValue(reader, writer, dataLine, updateInfo);
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error occurred in UpdateParametersWork", ex);
                return false;
            }

        }
    }
}
