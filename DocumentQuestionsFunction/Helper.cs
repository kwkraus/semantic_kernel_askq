﻿using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace DocumentQuestions.Function
{
   public class Helper(ILogger<Helper> log)
   {
      public async Task<(string filename, string question)> GetFilenameAndQuery(HttpRequestData req)
      {
         string filename = req.Query["filename"];
         string question = req.Query["question"];
         string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
         log.LogInformation(requestBody);
         dynamic data = JsonConvert.DeserializeObject(requestBody);
         filename ??= data?.filename;
         question ??= data?.question;

         if (string.IsNullOrWhiteSpace(filename))
         {
            filename = "general";
         }
         else
         {
            filename = Path.GetFileNameWithoutExtension(filename);
         }

         log.LogInformation($"filename = {filename}");
         log.LogInformation($"question = {question}");

         return (filename, question);
      }
   }
}
