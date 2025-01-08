using Azure;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentQuestions.Library
{
   public class AiSearch
   {
      private SearchIndexClient client;
      private ILogger<AiSearch> log;
      private IConfiguration config;

      public AiSearch(ILogger<AiSearch> log, IConfiguration config)
      {
         this.log = log;
         this.config = config;
         string endpoint = config[Constants.AISEARCH_ENDPOINT] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_ENDPOINT} in configuration");
         string key = config[Constants.AISEARCH_KEY] ?? throw new ArgumentException($"Missing {Constants.AISEARCH_KEY} in configuration");
         // Create a client
         //client = new SearchIndexClient(new Uri(endpoint), new DefaultAzureCredential());
         client = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(key));
      }

      public async Task<List<string>> ListAvailableIndexesAsync()
      {
         try
         {
            List<string> names = [];
            await foreach (var page in client.GetIndexNamesAsync())
            {
               names.Add($"\"{page}\"");
            }
            return names;
         }
         catch(Exception exe)
         {
            log.LogError($"Problem retrieving AI Search Idexes:\r\n{exe.Message}");
            return [];
         }
      }
   }
}
