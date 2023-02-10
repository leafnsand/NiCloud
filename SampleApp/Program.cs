using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NiCloud;
using NiCloud.Services;

namespace SampleApp;

public record PhotoData
{
    public string CheckSum { get; init; }
    public string Path { get; init; }
    public long Size { get; init; }
}

static class Program
{
    static async Task Main()
    {
        var serviceCollection = new ServiceCollection()
            .AddLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Trace));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var logger = serviceProvider.GetService<ILogger<NiCloudService>>();

        var sessionPath = Environment.GetEnvironmentVariable("session_path") ?? "session.json";
        var photoDbPath = Environment.GetEnvironmentVariable("photo_db_path") ?? "photo.db.json";

        NiCloudSession session;
        if (File.Exists(sessionPath))
        {
            var file = await File.ReadAllTextAsync(sessionPath);
            session = NiCloudSession.Deserialize(file);
        }
        else
        {
            session = new NiCloudSession();
        }

        Dictionary<string, PhotoData> photoDb;
        if (File.Exists(photoDbPath))
        {
            var file = await File.ReadAllTextAsync(photoDbPath);
            photoDb = JsonSerializer.Deserialize<Dictionary<string, PhotoData>>(file);
        }
        else
        {
            photoDb = new Dictionary<string, PhotoData>();
        }

        var api = new NiCloudService(session, logger);
        Console.WriteLine("Checking existing session");
        var sessionValid = await api.CheckSession();
        Console.WriteLine($"Result: {sessionValid}");

        if (!sessionValid)
        {
            var mail = Environment.GetEnvironmentVariable("username");
            var pass = Environment.GetEnvironmentVariable("password");

            if (string.IsNullOrEmpty(mail) || string.IsNullOrEmpty(pass))
            {
                Console.WriteLine("Empty username or password.");
                return;
            }
            
            await api.Init(mail, pass);

            if (api.Requires2fa)
            {
                var verification = await api.SendVerificationCode();
                Console.WriteLine("Two-factor authentication required.");
                Console.WriteLine($"Enter the code you received on your device with number: {verification?.TrustedPhoneNumber}");
                var code = Console.ReadLine();
                var result = await api.Validate2faCode(code);
                Console.WriteLine($"Code validation result: {result}");

                if (!result)
                {
                    Console.WriteLine("Failed to verify security code");
                    return;
                }
            }

            if (api.Requires2sa)
            {
                Console.WriteLine("Two-step authentication required. Your trusted devices are:");
                var devices = await api.GetTrustedDevices();

                var i = 0;
                foreach (var device in devices)
                {
                    Console.WriteLine(i + ") " + (device.DeviceName ?? $"SMS to {device.PhoneNumber}"));
                    i++;
                }

                Console.WriteLine("Which device to use?");
                if (!int.TryParse(Console.ReadLine(), out var choice))
                {
                    choice = 1;
                }

                var chosen = devices.ElementAtOrDefault(choice - 1) ?? devices.First();
                if (!await api.SendVerificationCode(chosen))
                {
                    Console.WriteLine("Failed to send verification code");
                }

                Console.WriteLine("Please enter validation code: ");
                var code = Console.ReadLine();
                var result = await api.ValidateVerificationCode(chosen, code);
                Console.WriteLine("Verification result " + result);
            }

            await File.WriteAllTextAsync(sessionPath, api.Session.Serialize());
        }

        // var driveApi = api.Drive();
        // var root = await driveApi.GetRoot();
        // var children = await root.GetChildren();

        var photosApi = api.Photos();

        await photosApi.Init();
        var albums = await photosApi.Albums();

        var album = albums.First(); // All Photos

        var files = new System.Collections.Concurrent.ConcurrentQueue<PhotoAsset>();

        var chunkNo = 0;

        await foreach (var chunk in album.GetPhotos(chunkNo))
        {
            chunkNo++;
            foreach (var photo in chunk)
            {
                files.Enqueue(photo);
            }
        }

        // await Parallel.ForEachAsync(Enumerable.Range(0, 34), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (chunk, _) =>
        // {
        //     await foreach (var photo in albums.First()
        //         .GetPhotos(chunk * 1000)
        //         .SelectMany(photos => photos.ToAsyncEnumerable())
        //         .Take(1000))
        //     {
        //         files.Enqueue(photo);
        //         Console.WriteLine(files.Count + " " + (files.Count / 33654.0 * 100));
        //     }
        // });

        var downloader = new HttpClient();
        var photos = files.ToList();
        foreach (var photo in photos)
        {
            try
            {
                if (!photoDb.TryAdd(photo.MasterRecord.RecordName, new PhotoData()
                    {
                        CheckSum = photo.Original.FileChecksum,
                        Path = $"{photo.CreateDate:yyyy-MM-dd}/{photo.FileName}",
                        Size = photo.Size,
                    }))
                {
                    Console.WriteLine($"Duplicate file: {photo.MasterRecord.RecordName}, {photo.CreateDate:yyyy-MM-dd}/{photo.FileName}");
                }
                // var path = @"contents\" + photo.FileName;
                // var fileInfo = new FileInfo(path);
                // var i = 0;
                // var name = Path.GetFileNameWithoutExtension(path);
                // var extension = Path.GetExtension(path);
                // if (photo.OriginalAlt != null)
                // {
                //
                // }
                //
                // while (fileInfo.Exists && fileInfo.Length != photo.Size)
                // {
                //     i++;
                //     path = @$"contents\{name} {i}{extension}";
                //     fileInfo = new FileInfo(path);
                // }
                //
                // if (fileInfo.Exists && fileInfo.Length == photo.Size && fileInfo.LastWriteTime != photo.CreateDate)
                // {
                //
                // }
                //
                // if (!fileInfo.Exists)
                // {
                //     using var stream = await downloader.GetStreamAsync(photo.Original.DownloadURL);
                //     using var sw = new FileStream(path, FileMode.Create);
                //     stream.CopyTo(sw);
                //     sw.Close();
                // }
                // File.SetLastWriteTime(path, photo.CreateDate);
                //
                // var livePhotoFileName = Path.ChangeExtension(path.Replace(@"contents\", @"contents\live\"), ".mov");
                // if (photo.LivePhoto != null && !File.Exists(livePhotoFileName))
                // {
                //     using var stream = await downloader.GetStreamAsync(photo.LivePhoto.DownloadURL);
                //     using var sw = new FileStream(livePhotoFileName, FileMode.Create);
                //     stream.CopyTo(sw);
                //     sw.Close();
                //     File.SetLastWriteTime(path, photo.CreateDate);
                // }
                //
                // var jpgFileName = Path.ChangeExtension(path.Replace(@"contents\", @"contents\jpg\"), ".jpg");
                // if (photo.JpegFull != null && !File.Exists(jpgFileName))
                // {
                //     using var stream = await downloader.GetStreamAsync(photo.JpegFull.DownloadURL);
                //     using var sw = new FileStream(jpgFileName, FileMode.Create);
                //     stream.CopyTo(sw);
                //     sw.Close();
                //     File.SetLastWriteTime(path, photo.CreateDate);
                // }
                //
                // Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
            }

            // var downloadInfos = await driveApi.GetDownloadInfo(children.Where(child => child.Type == NodeType.File));
            //
            // var tasks = downloadInfos.Select(async downloadInfo =>
            // {
            //     var fileName = Path.GetFileName(new Uri(downloadInfo.Data_token.Url).LocalPath);
            //     var stream = await driveApi.DownloadFile(downloadInfo);
            //
            //     Console.Write("Downloading " + fileName + "...");
            //     using var sw = new FileStream(@"contents\" + fileName, FileMode.Create);
            //     stream.CopyTo(sw);
            //     sw.Close();
            //     Console.WriteLine(" Success");
            // });
            //
            // await Task.WhenAll(tasks);
        }
            
        await File.WriteAllTextAsync(photoDbPath, JsonSerializer.Serialize(photoDb));
    }
}
