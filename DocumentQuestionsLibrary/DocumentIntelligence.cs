using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Logging;

namespace DocumentQuestions.Library
{
   public class DocumentIntelligence(
      ILogger<DocumentIntelligence> log, 
      SemanticUtility semanticUtility, 
      DocumentAnalysisClient documentAnalysisClient)
   {
      public async Task ProcessDocumentAsync(FileInfo file)
      {
         log.LogInformation($"Processing file {file.FullName} with Document Intelligence Service...");
         AnalyzeDocumentOperation operation;

         using (FileStream stream = new(file.FullName, FileMode.Open, FileAccess.Read))
         {
            operation = await documentAnalysisClient.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, "prebuilt-read", stream);
         }

         AnalyzeResult result = operation.Value;

         if (result != null)
         {
            log.LogInformation($"Parsing Document Intelligence results...");
            var contents = SplitDocumentIntoPagesAndParagraphs(result, file.Name);

            log.LogInformation($"Saving Document Intelligence results to Azure AI Search Index...");
            string memoryCollectionName = Common.ReplaceInvalidCharacters(Path.GetFileNameWithoutExtension(file.Name).ToLower());
            var taskList = new List<Task>
            {
               semanticUtility.StoreMemoryAsync(memoryCollectionName, contents),
               semanticUtility.StoreMemoryAsync("general", contents)
            };
            Task.WaitAll([.. taskList]);
         }

         log.LogInformation("Document Processed and Indexed");
      }
   
      private Dictionary<string, string> SplitDocumentIntoPagesAndParagraphs(AnalyzeResult result, string fileName)
      {
         var content = "";
         bool contentFound = false;
         var docContent = new Dictionary<string, string>();

         //Split by page if there is content...
         log.LogInformation("Checking document data...");

         foreach (DocumentPage page in result.Pages)
         {
            for (int i = 0; i < page.Lines.Count; i++)
            {
               DocumentLine line = page.Lines[i];
               log.LogDebug($"  Line {i} has content: '{line.Content}'.");
               content += line.Content.ToString();
               contentFound = true;
            }

            if (!string.IsNullOrEmpty(content))
            {
               log.LogDebug($"content = {content}");
               docContent.Add(GetFileName(fileName, page.PageNumber), content);
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
                     content += paragraph.Content + Environment.NewLine;
                  }
                  else
                  {
                     docContent.Add(GetFileName(fileName, counter), content);
                     counter++;

                     content = paragraph.Content + Environment.NewLine;
                  }
               }
            }

            //Add the last paragraph
            docContent.Add(GetFileName(fileName, counter), content);
         }

         return docContent;
      }

      private string GetFileName(string name, int counter)
      {
         string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
         string newName = nameWithoutExtension.Replace(".", "_");
         newName += $"_{counter.ToString().PadLeft(4, '0')}.json";
         return newName;
      }
   }
}