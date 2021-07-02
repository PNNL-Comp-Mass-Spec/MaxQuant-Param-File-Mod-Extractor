﻿using System;
using System.IO;
using PRISM;
using PRISM.Logging;

namespace MaxQuantParamFileModExtractor
{
    /// <summary>
    /// Main processing class
    /// </summary>
    internal static class Program
    {
        public const string PROGRAM_DATE = "July 2, 2021";

        // Ignore Spelling: Conf

        public static int Main(string[] args)
        {
            var programName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            var exePath = PRISM.FileProcessor.ProcessFilesOrDirectoriesBase.GetAppPath();
            var exeName = Path.GetFileName(exePath);
            var cmdLineParser = new CommandLineParser<ModExtractorOptions>(programName, GetAppVersion())
            {
                ProgramInfo = ConsoleMsgUtils.WrapParagraph(
                                   "This program parses a MaxQuant parameter file (XML-based) to extract the nodes that define static and dynamic mods."),
                ContactInfo = "Program written by Matthew Monroe for PNNL (Richland, WA) in 2021" + Environment.NewLine +
                                             "E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov" + Environment.NewLine +
                                             "Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/"
            };

            // ReSharper disable StringLiteralTypo

            cmdLineParser.UsageExamples.Add(exeName + " MaxQuant_Tryp_Stat_CysAlk_Dyn_MetOx_NTermAcet_20ppmParTol.xml");
            cmdLineParser.UsageExamples.Add(exeName + " /I:MaxQuant*.xml");

            // ReSharper restore StringLiteralTypo

            // The default argument name for parameter files is /ParamFile or -ParamFile
            // Also allow /Conf or /P
            cmdLineParser.AddParamFileKey("Conf");
            cmdLineParser.AddParamFileKey("P");

            var result = cmdLineParser.ParseArgs(args);
            var options = result.ParsedResults;
            if (!result.Success || !options.Validate())
            {
                // Delay for 750 msec in case the user double clicked this file from within Windows Explorer (or started the program via a shortcut)
                System.Threading.Thread.Sleep(750);
                return -1;
            }

            try
            {
                var extractor = new MaxQuantModExtractor(options);
                RegisterEvents(extractor);

                var success = extractor.ProcessFile(options.InputFilePath);

                if (success)
                {
                    return 0;
                }

                return -1;
            }
            catch (Exception ex)
            {
                ConsoleMsgUtils.ShowError("Error occurred in Program->Main", ex);
                return -1;
            }
        }

        private static string GetAppVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " (" + PROGRAM_DATE + ")";
        }

        /// <summary>Use this method to chain events between classes</summary>
        /// <param name="sourceClass"></param>
        private static void RegisterEvents(IEventNotifier sourceClass)
        {
            // Ignore: sourceClass.DebugEvent += OnDebugEvent;
            sourceClass.StatusEvent += OnStatusEvent;
            sourceClass.ErrorEvent += OnErrorEvent;
            sourceClass.WarningEvent += OnWarningEvent;
            // Ignore: sourceClass.ProgressUpdate += OnProgressUpdate;
        }

        private static void OnErrorEvent(string message, Exception ex)
        {
            ConsoleMsgUtils.ShowError(message, ex);
        }

        private static void OnStatusEvent(string message)
        {
            Console.WriteLine(message);
        }

        private static void OnWarningEvent(string message)
        {
            ConsoleMsgUtils.ShowWarning(message);
        }
    }
}
