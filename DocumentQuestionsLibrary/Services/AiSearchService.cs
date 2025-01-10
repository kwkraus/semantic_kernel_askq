using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Logging;

namespace DocumentQuestions.Library.Services
{
   public class AiSearchService(
      ILogger<AiSearchService> log,
      SearchIndexClient client)
   {
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
         catch (Exception exe)
         {
            log.LogError($"Problem retrieving AI Search Idexes:\r\n{exe.Message}");
            return [];
         }
      }
   }
}