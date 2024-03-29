﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Tavis.UriTemplates;

namespace ContentHubConsole.LogicApps
{
    public class DropboxLogicApp
    {
        public static long MaxFileSize = 52428800;
        public async Task<HttpResponseMessage> Send(LogicAppRequest request)
        {
            var log = $"Sending request to Dropbox logic app - {request.Filename}";
            Console.WriteLine(log);
            FileLogger.Log("DropboxLogicApp.Send.", log);

            HttpClient _client = new HttpClient();
            _client.Timeout = TimeSpan.FromHours(1);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Add("x-functions-key", "aGhNxTnTzYQW7CzSaWe3AMDw4mhF9gAPHWy03xBf/JHr97qg5yWjbA==");

            var payload = new StringContent(JsonConvert.SerializeObject(request));
            return await _client.PostAsync(Program.DropboxSingleFileUrl, payload);
        }
    }
}
