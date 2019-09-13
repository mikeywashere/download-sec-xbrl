﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.IO;
using System.Net;
using System.Xml.Linq;
using System.Net.Http;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace DownloadSecXbrl
{
    class Program
    {
        static HttpClient client = new HttpClient();

        static int requestCounter;

        static void timerTick(object state)
        {
            requestCounter = 0;
        }

        static async Task<string> Download()
        {
            var fileNumber = 0;
            var tempFolder = Path.GetTempPath();
            foreach (var file in Directory.GetFiles(tempFolder, "SEC-XBRL-0*.zip"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
            }

            var key = new object();

            ServicePointManager.DefaultConnectionLimit = 100;

            var rootFolder = "D:\\SecData\\xbrl";

            var filesCompleted = new HashSet<string>();

            for (int year = 2018; year < 2019; year++)
            {
                for (int month = 1; month < 13; month++)
                {
                    var folder = Path.Combine(rootFolder, year.ToString("0000"), month.ToString("00"));

                    Console.WriteLine(folder);

                    //var di = Directory.CreateDirectory(folder);

                    string data;
                    var url = $"https://sec.gov/Archives/edgar/monthly/xbrlrss-{year}-{month:00}.xml";
                    try
                    {
                        data = await client.GetStringAsync(url); // forbidden is done
                    }
                    catch
                    {
                        data = null;
                    }

                    if (data != null)
                    {

                        var doc = XDocument.Parse(data);
                        var all = from item in doc.Descendants()
                            where item.Name.LocalName == "item"
                            select new
                            {
                                title = item.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "title")
                                    ?.Value,
                                url = item.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "enclosure")
                                    ?.Attributes().FirstOrDefault(u => u.Name == "url")?.Value,
                                cik = item.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "cikNumber")
                                    ?.Value,
                                formType = item.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "formType")
                                    ?.Value.Replace("/", "-"),
                                fileNumber = item.DescendantsAndSelf()
                                    .FirstOrDefault(e => e.Name.LocalName == "fileNumber")?.Value,
                                filingDate = item.DescendantsAndSelf()
                                    .FirstOrDefault(e => e.Name.LocalName == "filingDate")?.Value,
                                xbrlFiles = item.DescendantsAndSelf()
                                    .FirstOrDefault(e => e.Name.LocalName == "xbrlFiles"),
                                inline = item.DescendantsAndSelf()?
                                    .FirstOrDefault(e => e.Name.LocalName == "xbrlFiles")?
                                    .DescendantsAndSelf().Where(f => f.Name.LocalName == "xbrlFile").Attributes()
                                    .Any(a => a.Name.LocalName == "inlineXBRL" && a.Value == "true")
                            };

                        var outstanding = 0;

                        var timer = new Timer(timerTick, null, 0, 1000);

                        Parallel.ForEach(all, new ParallelOptions() {MaxDegreeOfParallelism = 20}, async (item) =>
                        {
                            var isIxbrl = item.inline.HasValue && item.inline.Value;
                            if (!isIxbrl)
                            {
                                var endFolderName =
                                    $"{folder}\\{item.cik}.{item.formType}.{item.fileNumber}";

                                if (!Directory.Exists(endFolderName))
                                {
                                    if (item.url != null)
                                    {
                                        var fileNumberValue = Interlocked.Increment(ref fileNumber);
                                        var tempFileName = Path.Combine(tempFolder,
                                            $"SEC-XBRL-{fileNumberValue:0000000000}.zip");

                                        //var inline = (item.inline.HasValue && item.inline.Value) ? ".ixbrl" : "";
                                        var inline = string.Empty;
                                        var filingDate = new string(item.filingDate
                                            .Select(c => (Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))
                                            .ToArray());
                                        var filename =
                                            $"{item.cik}.{item.formType}.{filingDate}.{item.fileNumber}{inline}";

                                        var incr = 0;
                                        var newFilename = filename;

                                        lock (key)
                                            while (filesCompleted.Contains(newFilename))
                                            {
                                                newFilename = filename + "." + ++incr;
                                                Console.WriteLine($"\tDuplicate: {newFilename}");
                                            }

                                        filename = newFilename;

                                        lock (key)
                                            filesCompleted.Add(filename);

                                        var folderName = Path.Combine(folder, filename);

                                        if (!Directory.Exists(folderName))
                                        {
                                            Console.WriteLine($"\tGet: {outstanding}:{requestCounter} - {item.url}");

                                            while (outstanding > 50)
                                            {
                                                Thread.Sleep(1000);
                                            }

                                            Interlocked.Increment(ref outstanding);

                                            while (requestCounter > 9)
                                            {
                                                Thread.Sleep(25);
                                            }

                                            Interlocked.Increment(ref requestCounter);

                                            var directoryInfo = Directory.CreateDirectory(folderName);
                                            var response = await client.GetAsync(item.url);
                                            var xbrlData = await response.Content.ReadAsByteArrayAsync();

                                            Console.WriteLine($"\tWrite: {tempFileName}");
                                            await File.WriteAllBytesAsync(tempFileName, xbrlData).ContinueWith(
                                                canDecompress =>
                                                {
                                                    if (canDecompress.IsCompletedSuccessfully)
                                                    {
                                                        try
                                                        {
                                                            using (var fs = File.OpenRead(tempFileName))
                                                            {
                                                                var zf = new ZipFile(fs);
                                                                foreach (ZipEntry zipEntry in zf)
                                                                {
                                                                    if (!zipEntry.IsFile)
                                                                    {
                                                                        continue; // Ignore directories
                                                                    }

                                                                    var entryFileName = zipEntry.Name;
                                                                    // to remove the folder from the entry:- entryFileName = Path.GetFileName(entryFileName);
                                                                    // Optionally match entrynames against a selection list here to skip as desired.
                                                                    // The unpacked length is available in the zipEntry.Size property.

                                                                    var buffer = new byte[16384];
                                                                    var zipStream = zf.GetInputStream(zipEntry);

                                                                    // Manipulate the output filename here as desired.
                                                                    var fullZipToPath = Path.Combine(
                                                                        directoryInfo.FullName,
                                                                        entryFileName);
                                                                    var directoryName =
                                                                        Path.GetDirectoryName(fullZipToPath);
                                                                    if (directoryName.Length > 0)
                                                                        Directory.CreateDirectory(directoryName);

                                                                    if (!File.Exists(fullZipToPath))
                                                                    {
                                                                        Console.WriteLine(
                                                                            $"\tExtract: {fullZipToPath}");

                                                                        // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
                                                                        // of the file, but does not waste memory.
                                                                        // The "using" will close the stream even if an exception occurs.
                                                                        using (var streamWriter =
                                                                            File.Create(fullZipToPath))
                                                                        {
                                                                            StreamUtils.Copy(zipStream, streamWriter,
                                                                                buffer);
                                                                        }
                                                                    }
                                                                }

                                                                fs.Close();
                                                            }

                                                            File.Delete(tempFileName);
                                                        }
                                                        catch (ZipException ze)
                                                        {
                                                            Console.WriteLine(ze.Message);
                                                        }
                                                    }
                                                });
                                            Interlocked.Decrement(ref outstanding);
                                        }
                                    }
                                }
                            }
                        });
                    }
                }
            }

            return "Done";
        }

        static void Main(string[] args)
        {
            var t = Download();
            var u = t.Result;
            Console.WriteLine("Done");
            Console.ReadKey();
        }
    }
}