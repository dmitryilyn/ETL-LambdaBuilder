﻿using CommandLine.Text;
using CommandLine;
using org.ohdsi.cdm.framework.common.Enums;
using org.ohdsi.cdm.framework.common.Utility;
using System.Configuration;

namespace RunValidation
{
    internal class Program
    {
        internal class Options
        {
            [Option('v', "vendor", Required = true, HelpText = "Vendor name.")]
            public required string Vendor { get; set; }

            [Option('b', "buildingId", Required = true, HelpText = "Building ID.")]
            public required int BuildingId { get; set; }

            [Option('e', "etlLibraryPath", Default = "", HelpText = "(Optional) Path to a folder containing an external ETL .dll")]
            public string EtlLibraryPath { get; set; } = "";

            [Option('l', "localTmpPath", Default = "C:\\_tmp", HelpText = "(Optional) Path to local folder to contain intermediary data")]
            public string LocalTmpPath { get; set; } = "C:\\_tmp";

            [Option('c', "chunks", Separator = ',', HelpText = "(Optional) Comma-separated list of chunk IDs to process. All of them, if omitted.")]
            public IEnumerable<int> Chunks { get; set; } = new List<int>();

            [Usage(ApplicationAlias = "RunValidation")]
            public static IEnumerable<Example> Examples
            {
                get
                {
                    yield return new Example("Process all chunks", new Options 
                        { Vendor = "VendorName", BuildingId = 123});
                    yield return new Example("Process all chunks for an external .dll", new Options 
                        { Vendor = "ExternalVendorName", BuildingId = 123, EtlLibraryPath = "C:\\PathToExternalDllFolder"});
                    yield return new Example("Process specified chunks", new Options 
                        { Vendor = "VendorName", BuildingId = 123, Chunks = new List<int> { 1, 2, 3 } });
                }
            }
        }

        private static string _awsAccessKeyId => ConfigurationManager.AppSettings["awsAccessKeyId"] ?? throw new NullReferenceException("awsAccessKeyId");
        private static string _awsSecretAccessKey => ConfigurationManager.AppSettings["awsSecretAccessKey"] ?? throw new NullReferenceException("awsSecretAccessKey");
        private static string _bucket => ConfigurationManager.AppSettings["bucket"] ?? throw new NullReferenceException("bucket");
        private static string _cdmFolder => ConfigurationManager.AppSettings["cdmFolder"] ?? "cdmCSV";

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(RunWithOptions)
                .WithNotParsed(HandleParseError);

            Console.ReadLine();
        }

        static void RunWithOptions(Options opts)
        {
            var chunks = opts.Chunks.ToList() ?? new List<int>();

            Console.WriteLine("Options:");
            //Console.WriteLine($"Keys: {_awsAccessKeyId} - {_awsSecretAccessKey}");
            Console.WriteLine($"Bucket - folder: {_bucket} - {_cdmFolder}");

            Console.WriteLine($"Vendor: {opts.Vendor}");
            Console.WriteLine($"Building ID: {opts.BuildingId}");
            Console.WriteLine($"Chunks: {string.Join(", ", chunks)}");
            Console.WriteLine($"EtlLibraryPath: {opts.EtlLibraryPath}");
            Console.WriteLine($"LocalTmpPath: {opts.LocalTmpPath}");
            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine();

            Vendor vendor = EtlLibrary.CreateVendorInstance(opts.Vendor, opts.EtlLibraryPath);            
            var validation = new Validation(_awsAccessKeyId, _awsSecretAccessKey, _bucket, opts.LocalTmpPath, _cdmFolder);
            validation.ValidateBuildingId(vendor, opts.BuildingId, chunks);
        }

        static void HandleParseError(IEnumerable<Error> errs)
        {
            Console.WriteLine("Failed to parse command-line arguments.");
            foreach (var error in errs)
            {
                Console.WriteLine(error.ToString());
            }            
        }
    }
}
