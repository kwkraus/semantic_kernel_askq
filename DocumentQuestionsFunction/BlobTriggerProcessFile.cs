using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using DocumentQuestions.Library;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentQuestions.Library.Models;
using Azure.Identity;
using DocumentQuestions.Library.Services;
namespace DocumentQuestions.Function
{
   public class BlobTriggerProcessFile(
      ILoggerFactory logFactory, 
      IConfiguration config, 
      SemanticUtilityService semanticMemory, 
      DocumentAnalysisClient documentAnalysisClient)
   {
      private readonly ILogger<BlobTriggerProcessFile> log = logFactory.CreateLogger<BlobTriggerProcessFile>();

      [Function("BlobTriggerProcessFile")]
      public async Task RunAsync(
         [BlobTrigger("raw/{name}", Connection = "STORAGE_ACCOUNT_BLOB_URL")] Stream myBlob, 
         string name)
      {
         try
         {
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name}");
            string subscriptionKey = config[Constants.DOCUMENTINTELLIGENCE_KEY] ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_KEY} in configuration.");
            string endpoint = config[Constants.DOCUMENTINTELLIGENCE_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.DOCUMENTINTELLIGENCE_ENDPOINT} in configuration.");
            string storageAccountName = config[Constants.STORAGE_ACCOUNT_NAME] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_NAME} in configuration.");
            string memoryCollectionName = Path.GetFileNameWithoutExtension(name);

            log.LogInformation($"subkey =  {subscriptionKey}");
            log.LogInformation($"endpoint =  {endpoint}");

            semanticMemory.InitMemoryAndKernel();

            string imgUrl = $"https://{storageAccountName}.blob.core.windows.net/raw/{name}";

            log.LogInformation(imgUrl);

            Uri fileUri = new(imgUrl);

            log.LogInformation("About to get data from document intelligence module.");
            AnalyzeDocumentOperation operation = await documentAnalysisClient.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-read", fileUri);
            AnalyzeResult result = operation.Value;

            var content = "";
            bool contentFound = false;
            var taskList = new List<Task>();
            var docContent = new Dictionary<string, string>();

            //Split by page if there is content...
            foreach (DocumentPage page in result.Pages)
            {
               log.LogInformation("Checking out document data...");
               for (int i = 0; i < page.Lines.Count; i++)
               {
                  DocumentLine line = page.Lines[i];
                  log.LogDebug($"  Line {i} has content: '{line.Content}'.");
                  content += line.Content.ToString();
                  contentFound = true;
               }

               if (!string.IsNullOrEmpty(content))
               {
                  log.LogInformation("content = " + content);
                  taskList.Add(WriteAnalysisContentToBlobAsync(name, page.PageNumber, content, log));
                  docContent.Add(GetFileName(name, page.PageNumber), content);
               }
               content = "";
            }

            //Otherwise, split by collected paragraphs
            content = "";
            if (!contentFound && result.Paragraphs != null)
            {
               var counter = 0;
               foreach (DocumentParagraph paragraph in result.Paragraphs)
               {

                  if (paragraph != null && !string.IsNullOrWhiteSpace(paragraph.Content))
                  {
                     if (content.Length + paragraph.Content.Length < 4000)
                     {
                        content += paragraph.Content;
                     }
                     else
                     {
                        taskList.Add(WriteAnalysisContentToBlobAsync(name, counter, content, log));
                        docContent.Add(GetFileName(name, counter), content);
                        counter++;

                        content = paragraph.Content;
                     }
                  }
               }

               //Add the last paragraph
               taskList.Add(WriteAnalysisContentToBlobAsync(name, counter, content, log));
               docContent.Add(GetFileName(name, counter), content);
            }
            taskList.Add(semanticMemory.StoreMemoryAsync(memoryCollectionName, docContent));
            taskList.Add(semanticMemory.StoreMemoryAsync("general", docContent));

            Task.WaitAll(taskList.ToArray());
         }
         catch (Exception ex)
         {
            log.LogError(ex.Message);
         }
      }

      private string GetFileName(string name, int counter)
      {
         string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
         string newName = nameWithoutExtension.Replace(".", "_");
         newName += $"_{counter.ToString().PadLeft(4, '0')}.json";
         return newName;
      }

      private async Task<bool> WriteAnalysisContentToBlobAsync(string name, int counter, string content, ILogger log)
      {
         try
         {
            string newName = GetFileName(name, counter);
            string blobName = Path.GetFileNameWithoutExtension(name) + "/" + newName;

            var jsonObj = new ProcessedFile
            {
               FileName = name,
               BlobName = blobName,
               Content = content
            };

            string jsonStr = JsonConvert.SerializeObject(jsonObj);

            // Save the JSON string to Azure Blob Storage  
            string storageURL = config[Constants.STORAGE_ACCOUNT_BLOB_URL] ?? throw new ArgumentException($"Missing {Constants.STORAGE_ACCOUNT_BLOB_URL} in configuration.");
            string containerName = config[Constants.EXTRACTED_CONTAINER_NAME] ?? throw new ArgumentException($"Missing {Constants.EXTRACTED_CONTAINER_NAME} in configuration.");

            var blobServiceClient = new BlobServiceClient(new Uri(storageURL), new DefaultAzureCredential());
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            containerClient.CreateIfNotExists();

            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            using (var stream = new MemoryStream())
            {
               byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonStr);
               stream.Write(jsonBytes, 0, jsonBytes.Length);
               stream.Seek(0, SeekOrigin.Begin);
               await blobClient.UploadAsync(stream, overwrite: true);
            }

            log.LogInformation($"JSON file {newName} saved to Azure Blob Storage.");
            return true;
         }
         catch (Exception exe)
         {
            log.LogError("Unable to save file: " + exe.Message);
            return false;
         }
      }
   }
}
