﻿using Azure;
using Microsoft.VisualBasic.FileIO;
using Stylelabs.M.Base.Querying;
using Stylelabs.M.Base.Querying.Linq;
using Stylelabs.M.Sdk.WebClient;
using Stylelabs.M.Sdk.WebClient.Models.Upload;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tavis.UriTemplates;
using SearchOption = System.IO.SearchOption;

namespace ContentHubConsole.Assets
{
    public class UploadManager
    {
        private readonly IWebMClient _webMClient;
        private string _contentHubUrl;
        private string _contentHubToken;
        private const long MAX_FILE_SIZE_BYTES = 1000000;
        private const string UPLOAD_CONFIGURATION = "ApprovedAssetUploadConfiguration";

        List<string> _largeFilePaths = new List<string>();
        List<FileUploadResponse> _largeFileVersions = new List<FileUploadResponse>();
        public List<FileUploadResponse> DirectoryFileUploadResponses = new List<FileUploadResponse>();

        public UploadManager(IWebMClient webMClient)
        {
            _webMClient = webMClient;
        }

        public UploadManager(IWebMClient webMClient, string contentHubUrl, string token) : this(webMClient)
        {
            _contentHubUrl = contentHubUrl;
            _contentHubToken = token;
        }

        public async Task UploadMissingFiles(List<FileUploadResponse> fileUploadResponses)
        {
            Console.WriteLine($"Uploading mising files");
            FileLogger.Log("UploadMissingFiles", $"Uploading mising files");

            try
            {
                var uploadTasks = new List<Task>();

                foreach (var file in fileUploadResponses)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file.LocalPath);
                        if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                        {
                            _largeFilePaths.Add(file.LocalPath);
                        }
                        else
                        {
                            uploadTasks.Add(UploadLocalFile(file.LocalPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        DirectoryFileUploadResponses.Add(new FileUploadResponse(0, file.LocalPath));

                        Console.WriteLine(ex.Message);
                        FileLogger.Log("UploadMissingFiles", ex.Message);
                        continue;
                    }
                }

                await Task.WhenAll(uploadTasks);

                try
                {
                    foreach (var task in uploadTasks)
                    {
                        var result = ((Task<FileUploadResponse>)task).Result;
                        DirectoryFileUploadResponses.Add(result);
                    }
                }
                catch { }

                var log = $"Done uploading missing files. Count {DirectoryFileUploadResponses.Where(w => w.AssetId > 0).Count()}";
                Console.WriteLine(log);
                FileLogger.Log("UploadLocalDirectory", log);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLocalDirectory", ex.Message);
            }

        }

        public async Task<FileUploadResponse> UploadLocalFile(string path)
        {
            
            var fileUploadResponse = new FileUploadResponse(0, path);

            try
            {
                var fileInfo = new FileInfo(path);
                byte[] fileBytes = File.ReadAllBytes(path);

                if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                {
                    return await UploadLargeLocalFile(path);
                }

                var uploadSource = new LocalUploadSource(path, fileInfo.Name);
                var request = new UploadRequest(uploadSource, UPLOAD_CONFIGURATION, "NewAsset");

                // Initiate upload and wait for its completion.
                var response = await _webMClient.Uploads.UploadAsync(request).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    // Extract ID of newly created asset from the location header.
                    var responseId = await _webMClient.LinkHelper.IdFromEntityAsync(response.Headers.Location).ConfigureAwait(false);
                    fileUploadResponse.AssetId = responseId ?? 0;
                }

                return fileUploadResponse;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLocalFile", ex.Message);
                return fileUploadResponse;
            }
        }

        public async Task<FileUploadResponse> UploadLocalFileVersion(FileUploadResponse fileResp)
        {
            try
            {
                var fileInfo = new FileInfo(fileResp.LocalPath);
                byte[] fileBytes = File.ReadAllBytes(fileResp.LocalPath);

                if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                {
                    return await UploadLargeLocalFile(fileResp.LocalPath);
                }

                var uploadSource = new LocalUploadSource(fileResp.LocalPath, fileInfo.Name);
                var request = new UploadRequest(uploadSource, UPLOAD_CONFIGURATION, "NewMainFile");
                request.ActionParameters = new Dictionary<string, object>();
                request.ActionParameters.Add(new KeyValuePair<string, object>("AssetId", fileResp.AssetId));

                // Initiate upload and wait for its completion.
                var response = await _webMClient.Uploads.UploadAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    fileResp.AssetId = 0;
                }

                return fileResp;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLocalFile", ex.Message);
                return fileResp;
            }
        }

        public async Task<FileUploadResponse> UploadLargeLocalFile(string path)
        {
            var fileUploadResponse = new FileUploadResponse(0, path);

            try
            {
                var fileInfo = new FileInfo(path);
                byte[] fileBytes = File.ReadAllBytes(path);

                var uploadSource = new LocalUploadSource(path, fileInfo.Name);
                var request = new UploadRequest(uploadSource, UPLOAD_CONFIGURATION, "NewAsset");

                var uploadRequest = new LargeUploadRequest(fileInfo.Name,
                    GetMediaType(fileInfo.Extension),
                    fileBytes.LongLength,
                    fileBytes,
                    _contentHubUrl,
                    _contentHubToken,
                    UPLOAD_CONFIGURATION);

                var largeUpload = new LargeFileUpload();
                var largeFileResp = await largeUpload.Upload(uploadRequest);
                fileUploadResponse.AssetId = largeFileResp.Asset_id;
                return fileUploadResponse;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLargeLocalFile", ex.Message);
                return fileUploadResponse;
            }
        }

        public async Task<FileUploadResponse> UploadLargeLocalFileVersion(FileUploadResponse fileresp)
        {
            try
            {
                var fileInfo = new FileInfo(fileresp.LocalPath);
                byte[] fileBytes = File.ReadAllBytes(fileresp.LocalPath);

                var uploadSource = new LocalUploadSource(fileresp.LocalPath, fileInfo.Name);
                var request = new UploadRequest(uploadSource, UPLOAD_CONFIGURATION, "NewMainFile");
                request.ActionParameters = new Dictionary<string, object>();
                request.ActionParameters.Add(new KeyValuePair<string, object>("AssetId", fileresp.AssetId));

                var uploadRequest = new LargeUploadRequest(fileInfo.Name,
                    GetMediaType(fileInfo.Extension),
                    fileBytes.LongLength,
                    fileBytes,
                    _contentHubUrl,
                    _contentHubToken,
                    UPLOAD_CONFIGURATION);

                var largeUpload = new LargeFileUpload();
                var largeFileResp = await largeUpload.Upload(uploadRequest, fileresp.AssetId);
                return fileresp;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLargeLocalFileVersion", ex.Message);
                return fileresp;
            }
        }

        public async Task UploadLocalDirectory(string directoryPath, SearchOption searchOption = SearchOption.AllDirectories)
        {
            Console.WriteLine($"Getting files from directory: {directoryPath}");
            FileLogger.Log("UploadLocalDirectory", $"Getting files from directory: {directoryPath}");

            try
            {
                var uploadTasks = new List<Task>();

                //List<string> existingFiles = await GetExistingUploads();
                var files = Program.TestMode ? Directory.GetFiles(directoryPath, "*.*", searchOption).Take(1).ToArray() : Directory.GetFiles(directoryPath, "*.*", searchOption);//.Except(existingFiles).ToList();
                Console.WriteLine($"Total files from directory: {files.Count()}");
                FileLogger.Log("UploadLocalDirectory", $"Total files from directory: {files.Count()}");
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                        {
                            _largeFilePaths.Add(file);
                        }
                        else
                        {
                            uploadTasks.Add(UploadLocalFile(file));
                        }
                    }
                    catch (Exception ex)
                    {
                        DirectoryFileUploadResponses.Add(new FileUploadResponse(0, file));

                        Console.WriteLine(ex.Message);
                        FileLogger.Log("UploadLocalDirectory", ex.Message);
                        continue;
                    }
                }

                await Task.WhenAll(uploadTasks);

                try
                {
                    foreach (var task in uploadTasks)
                    {
                        var result = ((Task<FileUploadResponse>)task).Result;
                        DirectoryFileUploadResponses.Add(result);
                    }
                }
                catch { }

                var log = $"Done getting non-large files from directory: {directoryPath}";
                Console.WriteLine(log);
                FileLogger.Log("UploadLocalDirectory", log);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLocalDirectory", ex.Message);
            }
        }

        public async Task UploadLocalDirectoryVersions(List<FileUploadResponse> fileUploadResponses)
        {
            Console.WriteLine($"Uploading versions - count: {fileUploadResponses.Count}");
            FileLogger.Log("UploadLocalDirectoryVersions", $"Uploading versions - count: {fileUploadResponses.Count}");

            try
            {
                var uploadTasks = new List<Task>();

                foreach (var file in fileUploadResponses)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file.LocalPath);
                        if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                        {
                            _largeFileVersions.Add(file);
                        }
                        else
                        {
                            uploadTasks.Add(UploadLocalFileVersion(file));
                        }
                    }
                    catch (Exception ex)
                    {
                        DirectoryFileUploadResponses.Add(file);

                        Console.WriteLine(ex.Message);
                        FileLogger.Log("UploadLocalDirectoryVersions", ex.Message);
                        continue;
                    }
                }

                await Task.WhenAll(uploadTasks);

                try
                {
                    foreach (var task in uploadTasks)
                    {
                        var result = ((Task<FileUploadResponse>)task).Result;
                        DirectoryFileUploadResponses.Add(result);
                    }
                }
                catch { }

                var log = $"Done uploading file versions";
                Console.WriteLine(log);
                FileLogger.Log("UploadLocalDirectoryVersions", log);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLocalDirectoryVersions", ex.Message);
            }
        }

        private async Task<List<string>> GetExistingUploads()
        {
            var results = new List<string>();
            try
            {
                var query = Query.CreateQuery(entities =>
                 (from e in entities
                  where e.DefinitionName == "M.Asset" && e.Property("OriginPath").Contains("SmartPak") && e.Property("OriginPath").Contains("Logos")
                  select e).Skip(0).Take(5000));

                var mq = await _webMClient.Querying.QueryAsync(query);

                if (mq.Items.Any())
                {
                    Console.WriteLine($"Found: {mq.Items.Count}");
                    foreach (var item in mq.Items.ToList())
                    {
                        var path = item.GetPropertyValue<string>("OriginPath");
                        results.Add($"C:\\Users\\ptjhi{path}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }

            return results;
        }

        public async Task UploadLargeFileLocalDirectory()
        {
            Console.WriteLine($"Getting Large files from directory.");
            FileLogger.Log("UploadLargeFileLocalDirectory", "Getting Large files from directory.");

            try
            {
                var largeFileUploadTasks = new List<Task>();

                foreach (var file in _largeFilePaths)
                {
                    largeFileUploadTasks.Add(UploadLargeLocalFile(file));
                }

                await Task.WhenAll(largeFileUploadTasks);

                try
                {
                    foreach (var task in largeFileUploadTasks)
                    {
                        var result = ((Task<FileUploadResponse>)task).Result;
                        DirectoryFileUploadResponses.Add(result);
                    }
                }
                catch { }

                var log = "Done getting large files from directory.";
                Console.WriteLine(log);
                FileLogger.Log("UploadLargeFileLocalDirectory", log);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLargeFileLocalDirectory", ex.Message);
            }
        }

        public async Task UploadLargeFileLocalDirectoryVersions()
        {
            Console.WriteLine($"Getting Large files from directory.");
            FileLogger.Log("UploadLargeFileLocalDirectory", "Getting Large files from directory.");

            try
            {
                var largeFileUploadTasks = new List<Task>();

                foreach (var file in _largeFileVersions)
                {
                    largeFileUploadTasks.Add(UploadLargeLocalFileVersion(file));
                }

                await Task.WhenAll(largeFileUploadTasks);

                try
                {
                    foreach (var task in largeFileUploadTasks)
                    {
                        var result = ((Task<FileUploadResponse>)task).Result;
                        DirectoryFileUploadResponses.Add(result);
                    }
                }
                catch { }

                var log = "Done getting large files from directory.";
                Console.WriteLine(log);
                FileLogger.Log("UploadLargeFileLocalDirectoryVersions", log);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UploadLargeFileLocalDirectoryVersions", ex.Message);
            }
        }


        public static string GetMediaType(string extension)
        {
            return extension switch
            {
                _ when extension.Equals(".jpg", StringComparison.InvariantCultureIgnoreCase) => "image/jpeg",
                _ when extension.Equals(".jpeg", StringComparison.InvariantCultureIgnoreCase) => "image/jpeg",
                _ when extension.Equals(".gif", StringComparison.InvariantCultureIgnoreCase) => "image/gif",
                _ when extension.Equals(".png", StringComparison.InvariantCultureIgnoreCase) => "image/png",
                _ when extension.Equals(".tiff", StringComparison.InvariantCultureIgnoreCase) => "image/tiff",
                _ when extension.Equals(".tif", StringComparison.InvariantCultureIgnoreCase) => "image/tiff",
                _ when extension.Equals(".ttf", StringComparison.InvariantCultureIgnoreCase) => "font/ttf",
                _ when extension.Equals(".ttc", StringComparison.InvariantCultureIgnoreCase) => "font/ttc",
                _ when extension.Equals(".lst", StringComparison.InvariantCultureIgnoreCase) => "text/plain",
                _ when extension.Equals(".mp4", StringComparison.InvariantCultureIgnoreCase) => "video/mp4",
                _ when extension.Equals(".mov", StringComparison.InvariantCultureIgnoreCase) => "video/quicktime",
                _ when extension.Equals(".m4v", StringComparison.InvariantCultureIgnoreCase) => "video/x-m4v",
                _ when extension.Equals(".zip", StringComparison.InvariantCultureIgnoreCase) => "application/zip",
                _ when extension.Equals(".pdf", StringComparison.InvariantCultureIgnoreCase) => "application/pdf",
                _ when extension.Equals(".json", StringComparison.InvariantCultureIgnoreCase) => "application/json",
                _ when extension.Equals(".xml", StringComparison.InvariantCultureIgnoreCase) => "application/xml",
                _ when extension.Equals(".rar", StringComparison.InvariantCultureIgnoreCase) => "application/x-rar-compressed",
                _ when extension.Equals(".gzip", StringComparison.InvariantCultureIgnoreCase) => "application/x-gzip",
                _ when extension.Equals(".ps1", StringComparison.InvariantCultureIgnoreCase) => "text/plain",
                _ when extension.Equals(".csv", StringComparison.InvariantCultureIgnoreCase) => "text/plain",
                _ when extension.Equals(".psd", StringComparison.InvariantCultureIgnoreCase) => "image/vnd.adobe.photoshop",
                _ when extension.Equals(".js", StringComparison.InvariantCultureIgnoreCase) => "text/javascript",
                _ when extension.Equals(".xlsx", StringComparison.InvariantCultureIgnoreCase) => "application/vnd.ms-excel",
                _ when extension.Equals(".html", StringComparison.InvariantCultureIgnoreCase) => "text/html",
                _ when extension.Equals(".ai", StringComparison.InvariantCultureIgnoreCase) => "application/postscript",
                _ when extension.Equals(".eps", StringComparison.InvariantCultureIgnoreCase) => "application/postscript",
                _ when extension.Equals(".indd", StringComparison.InvariantCultureIgnoreCase) => "application/x-indesign",
                _ when extension.Equals(".idml", StringComparison.InvariantCultureIgnoreCase) => "application/vnd.adobe.indesign-idml-package",
                _ when extension.Equals(".jpf", StringComparison.InvariantCultureIgnoreCase) => "image/x-jpf",
                _ when extension.Equals(".cr2", StringComparison.InvariantCultureIgnoreCase) => "image/x-dcraw",
                _ when extension.Equals(".svg", StringComparison.InvariantCultureIgnoreCase) => "image/svg+xml",
                _ when extension.Equals(".sketch", StringComparison.InvariantCultureIgnoreCase) => "application/sketch",
                _ when extension.Equals(".fig", StringComparison.InvariantCultureIgnoreCase) => "application/fig",
                _ when extension.Equals(".pptx", StringComparison.InvariantCultureIgnoreCase) => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                _ => throw new NotImplementedException()
            };
        }
    }
}
