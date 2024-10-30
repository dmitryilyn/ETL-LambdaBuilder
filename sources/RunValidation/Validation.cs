﻿using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using CsvHelper.Configuration;
using CsvHelper;
using org.ohdsi.cdm.framework.common.Enums;
using org.ohdsi.cdm.framework.common.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using ZstdSharp;
using Spectre.Console;
using System.Collections.Concurrent;

namespace RunValidation
{

    public class Validation(string awsAccessKeyId, string awsSecretAccessKey, string bucket, string tmpFolder, string cdmFolder)
    {
        #region Fields 

        private readonly string _awsAccessKeyId = awsAccessKeyId;
        private readonly string _awsSecretAccessKey = awsSecretAccessKey;
        private readonly string _bucket = bucket;
        private readonly string _tmpFolder = tmpFolder;
        private readonly string _cdmFolder = cdmFolder;
        private readonly LambdaUtility _lambdaUtility = 
            new LambdaUtility(awsAccessKeyId, awsSecretAccessKey, awsAccessKeyId, awsSecretAccessKey, bucket, bucket, bucket, cdmFolder);


        #endregion

        #region Methods

        public void ValidateBuildingId(Vendor vendor, int buildingId, List<int> chunks)
        {
            var _wrong = new List<string>();
            var timer = new Stopwatch();
            timer.Start();

            var actualSlices = GetActualSlices(vendor.Name, buildingId).OrderBy(s => s).ToList();

            int chunkCount = 0;
            var actualChunks = new List<KeyValuePair<int, Dictionary<long, List<string>>>>();

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Getting all chunks...", ctx =>
                {
                    var progress = new Progress<int>(count =>
                    {
                        chunkCount = count;
                        ctx.Status($"Getting all chunks... (Chunks obtained: {chunkCount})");
                    });

                    foreach (var chunk in GetChunks(vendor, buildingId, progress))
                    {
                        actualChunks.Add(chunk);
                    }
                });

            timer.Stop();
            AnsiConsole.MarkupLine($"[green]Getting all {actualChunks.Count} chunks done. It took {timer.ElapsedMilliseconds / 1000}s[/]");
            timer.Restart();

            ProcessChunksWithProgress(vendor, buildingId, chunks, actualSlices, actualChunks);

            timer.Stop();
            AnsiConsole.MarkupLine($"[green]Done. Problematic chunks, if any, are described above the chunk progress. Total seconds={timer.ElapsedMilliseconds / 1000}s[/]");
            timer.Restart();
        }




        private IEnumerable<KeyValuePair<int, Dictionary<long, List<string>>>> GetChunks(Vendor vendor, int buildingId, IProgress<int> progress)
        {
            var currentChunkId = 0;
            var result = new KeyValuePair<int, Dictionary<long, List<string>>>(0, new Dictionary<long, List<string>>());
            var prefix = $"{vendor}/{buildingId}/_chunks";
            int chunkCount = 0;

            using (var client = new AmazonS3Client(_awsAccessKeyId, _awsSecretAccessKey, Amazon.RegionEndpoint.USEast1))
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix
                };

                var response = client.ListObjectsV2Async(request);
                response.Wait();

                foreach (var o in response.Result.S3Objects.OrderBy(o => o.LastModified))
                {
                    using var transferUtility = new TransferUtility(_awsAccessKeyId, _awsSecretAccessKey, Amazon.RegionEndpoint.USEast1);
                    using var responseStream = transferUtility.OpenStream(_bucket, o.Key);
                    using var bufferedStream = new BufferedStream(responseStream);
                    using var gzipStream = new GZipStream(bufferedStream, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzipStream, Encoding.Default);
                    string? line = reader.ReadLine();
                    while (!string.IsNullOrEmpty(line))
                    {
                        var chunkId = int.Parse(line.Split('\t')[0]);

                        if (currentChunkId != chunkId)
                        {
                            if (result.Value.Count > 0)
                            {
                                yield return result;
                                chunkCount++;
                                progress?.Report(chunkCount);
                            }

                            result = new KeyValuePair<int, Dictionary<long, List<string>>>(chunkId, new Dictionary<long, List<string>>());
                            currentChunkId = chunkId;
                        }

                        var personId = long.Parse(line.Split('\t')[1]);
                        result.Value.Add(personId, new List<string>());

                        line = reader.ReadLine();
                    }
                }
            }

            if (result.Value.Count > 0)
            {
                yield return result;
                chunkCount++;
                progress?.Report(chunkCount);
            }
        }



        private void ProcessChunksWithProgress(Vendor vendor, int buildingId, List<int> chunks, List<int> actualSlices, List<KeyValuePair<int, Dictionary<long, List<string>>>> actualChunks)
        {
            AnsiConsole.MarkupLine("\n");

            AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .Start(ctx =>
                {
                    var tasks = new ConcurrentDictionary<int, ProgressTask>();

                    foreach (var awsChunk in actualChunks)
                    {
                        var awsChunkId = awsChunk.Key;

                        if (chunks.Any() && !chunks.Contains(awsChunkId))
                        {
                            continue;
                        }

                        var task = ctx.AddTask($"Chunk {awsChunkId}", maxValue: actualSlices.Count);
                        tasks[awsChunkId] = task;
                    }

                    Parallel.ForEach(actualChunks
                        , new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount == 1 ? 1 : Environment.ProcessorCount - 1 } // leave 1 core for UI and OS
                        , awsChunk =>
                    {
                        var awsChunkId = awsChunk.Key;
                        var awsChunkPersonIds = awsChunk.Value.Keys.ToHashSet();

                        if (tasks.TryGetValue(awsChunkId, out var task))
                        {
                            ValidateChunkIdWithProgress(vendor, buildingId, awsChunkId, awsChunkPersonIds, actualSlices, task);
                        }
                    });
                });
        }



        private void  ValidateChunkIdWithProgress(Vendor vendor, int buildingId, int chunkId, HashSet<long> chunkPersonIds, List<int> slices, ProgressTask task)
        {
            var s3ObjectsBySlice = GetS3ObjectsBySlice(vendor, buildingId, chunkId, slices);

            var personIdsInBatchAndPersonOrMetadata = new HashSet<long>();

            foreach (var slice in s3ObjectsBySlice)
            {
                var slicePersonIdsInBatchAndPersonOrMetadata = ValidateSliceId(vendor, buildingId, chunkId, slice.Key, chunkPersonIds, slice.Value.personObjects, slice.Value.metadataObjects);
                foreach (var slicePersonId in slicePersonIdsInBatchAndPersonOrMetadata)
                    personIdsInBatchAndPersonOrMetadata.Add(slicePersonId);
                task.Increment(1);
            }

            var inBatchOnlyPersonIds = chunkPersonIds
                .Where(s => !personIdsInBatchAndPersonOrMetadata.TryGetValue(s,  out long actualValue))
                .ToHashSet();

            if (inBatchOnlyPersonIds.Count > 0)
            {
                var msg = $"[red]BuildingId={buildingId} ChunkId={chunkId} | InBatchOnlyIdsCount={inBatchOnlyPersonIds.Count} | Id Example={inBatchOnlyPersonIds.First()}[/]";
                AnsiConsole.MarkupLine(msg);
            }

        }


        private HashSet<int> GetActualSlices(string vendorName, int buildingId)
        {
            var slices = new HashSet<int>();
            var prefix = $"{vendorName}/{buildingId}/{_cdmFolder}/PERSON/PERSON.";
            Console.WriteLine("Calculating slices " + _bucket + "|" + prefix);
            using (var client = new AmazonS3Client(_awsAccessKeyId, _awsSecretAccessKey, Amazon.RegionEndpoint.USEast1))
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix
                };
                ListObjectsV2Response response;

                do
                {
                    var responseTask = client.ListObjectsV2Async(request);
                    responseTask.Wait();
                    response = responseTask.Result;

                    foreach (var o in response.S3Objects)
                    {
                        slices.Add(int.Parse(o.Key.Split('.')[1]));
                    }

                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated);
            }

            Console.WriteLine("slices.Count=" + slices.Count);
            Console.WriteLine();

            return slices;
        }

        private Dictionary<int, (List<S3Object> personObjects, List<S3Object> metadataObjects)> GetS3ObjectsBySlice(Vendor vendor, 
            int buildingId, int chunkId, List<int> slices2process)
        {
            var s3ObjectsBySlice = new Dictionary<int, (List<S3Object> PersonObjects, List<S3Object> MetadataObjects)>();

            foreach (var tuple in GetObjects(vendor, buildingId, "PERSON", chunkId, slices2process))
            {
                int sliceId = tuple.Item1;
                List<S3Object> personObjects = tuple.Item2;

                if (!s3ObjectsBySlice.ContainsKey(sliceId))
                    s3ObjectsBySlice[sliceId] = (new List<S3Object>(), new List<S3Object>());

                s3ObjectsBySlice[sliceId].PersonObjects.AddRange(personObjects);
            }

            foreach (var tuple in GetObjects(vendor, buildingId, "METADATA_TMP", chunkId, slices2process))
            {
                int sliceId = tuple.Item1;
                List<S3Object> metadataObjects = tuple.Item2;

                if (!s3ObjectsBySlice.ContainsKey(sliceId))
                    s3ObjectsBySlice[sliceId] = (new List<S3Object>(), new List<S3Object>());

                s3ObjectsBySlice[sliceId].MetadataObjects.AddRange(metadataObjects);
            }

            if (s3ObjectsBySlice.Count == 0)
            {
                var msg = $"chunkId={chunkId} - MISSED";
                Console.WriteLine(msg);
            }

            return s3ObjectsBySlice;
        }

        private IEnumerable<Tuple<int, List<S3Object>>> GetObjects(Vendor vendor, int buildingId, string table, int chunkId, List<int> slices)
        {
            var orderedSlices = slices.Distinct().OrderBy(s => s).ToList();
            for (int i = 0; i < orderedSlices.Count; i++)
            {
                var prefix = $"{vendor}/{buildingId}/{_cdmFolder}/{table}/{table}.{orderedSlices[i]}.{chunkId}.";
                using var client = new AmazonS3Client(_awsAccessKeyId, _awsSecretAccessKey, Amazon.RegionEndpoint.USEast1);
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix
                };
                ListObjectsV2Response response;
                do
                {
                    response = client.ListObjectsV2Async(request).GetAwaiter().GetResult();
                    yield return Tuple.Create(orderedSlices[i], response.S3Objects);
                    request.ContinuationToken = response.NextContinuationToken;
                } 
                while (response.IsTruncated);
            }
        }

        private HashSet<long> ValidateSliceId(Vendor vendor, int buildingId, int chunkId, int sliceId, HashSet<long> chunkPersonIds,
            List<S3Object> personObjects, List<S3Object> metadataObjects)
        {
            var attempt = 0;
            var complete = false;

            while (!complete)
            {
                try
                {
                    attempt++;
                    var timer = new Stopwatch();
                    timer.Start();

                    #region var appearanceStatsByPersonId = new Dictionary<long, (int InPersonCount, int InMetadataCount)>();
                    var appearanceStatsByPersonId = new Dictionary<long, (int InPersonCount, int InMetadataCount)>();


                    var cnt = 0;
                    var attempt1 = attempt;

                    var allObjects = personObjects.Union(metadataObjects).ToList();

                    Parallel.ForEach(allObjects, o =>
                    {
                        using var transferUtility = new TransferUtility(_awsAccessKeyId, _awsSecretAccessKey, Amazon.RegionEndpoint.USEast1);
                        using var responseStream = transferUtility.OpenStream(_bucket, o.Key);
                        using var bufferedStream = new BufferedStream(responseStream);
                        using Stream compressedStream = o.Key.EndsWith(".gz")
                            ? new GZipStream(bufferedStream, CompressionMode.Decompress)
                            : new DecompressionStream(bufferedStream) //.zst
                            ;
                        using var reader = new StreamReader(compressedStream, Encoding.Default);
                        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                        {
                            HasHeaderRecord = false,
                            Delimiter = ",",
                            Encoding = Encoding.UTF8
                        });
                        while (csv.Read())
                        {
                            var personId = (long)csv.GetField(typeof(long), 0);
                            lock (appearanceStatsByPersonId)
                            {
                                if (!appearanceStatsByPersonId.ContainsKey(personId))
                                    appearanceStatsByPersonId[personId] = (0, 0);

                                var tuple = appearanceStatsByPersonId[personId];

                                if (o.Key.Contains("PERSON"))
                                    tuple.InPersonCount++;
                                else if (o.Key.Contains("METADATA_TMP"))
                                    tuple.InMetadataCount++;

                                appearanceStatsByPersonId[personId] = tuple;
                            }
                        }
                        Interlocked.Increment(ref cnt);
                    });

                    #endregion

                    var appearanceStatsByChunkPersonId = chunkPersonIds
                        .Select(s => appearanceStatsByPersonId.ContainsKey(s)
                                     ? KeyValuePair.Create(s, Tuple.Create(appearanceStatsByPersonId[s].InPersonCount, appearanceStatsByPersonId[s].InMetadataCount))
                                     : KeyValuePair.Create(s, Tuple.Create(0, 0)))
                        .ToDictionary();

                    int wrongCount = 0;
                    var dups = 0;
                    var personIdsInBatchAndPersonOrMetadata = new HashSet<long>();
                    var wrongPersonIds = new HashSet<long>();

                    foreach (var kvp in appearanceStatsByPersonId)
                    {
                        var personId = kvp.Key;
                        var stats = kvp.Value;

                        if(stats.InPersonCount > 0 || stats.InMetadataCount > 0)
                            personIdsInBatchAndPersonOrMetadata.Add(personId);

                        //check InPersonCount just in case, InMetadataCount should actually suffice
                        if (stats.InPersonCount > 1 || stats.InMetadataCount > 0)
                        {
                            wrongCount++;
                                                        
                            if (stats.InPersonCount > 1 || stats.InMetadataCount > 1)
                                dups++;

                            if(!wrongPersonIds.Contains(personId))
                                wrongPersonIds.Add(personId);
                        }
                    }

                    timer.Stop();

                    if (wrongCount > 0)
                    {
                        var msg = $"[red]BuildingId={buildingId} ChunkId={chunkId} SliceId={sliceId} | WrongCount={wrongCount}; Duplicates={dups} | Wrong Person Id Example={wrongPersonIds.First()}[/]";
                        AnsiConsole.MarkupLine(msg);
                    }

                    complete = true;
                    return personIdsInBatchAndPersonOrMetadata;
                }
                catch (Exception ex)
                {
                    var msg = $"[red]{ex.Message} | ProcessChunk Exception | new attempt | attempt={attempt}[/]";
                    AnsiConsole.MarkupLine(msg);
                    if (attempt > 3)
                    {
                        throw;
                    }
                }
            }
            return null;
        }

        #endregion

    }
}