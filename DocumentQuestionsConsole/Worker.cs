﻿using DocumentQuestions.Library;
using DocumentQuestions.Library.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine.Parsing;
using System.Reflection;
using syS = System;

namespace DocumentQuestions.Console
{
   internal class Worker : BackgroundService
   {
      private static ILogger<Worker> logger;
      private static IConfiguration config;
      private static StartArgs startArgs;
      private static SemanticUtilityService semanticUtility;
      private static Parser rootParser;
      private static DocumentIntelligenceService documentIntelligence;
      private static string activeDocument = string.Empty;
      private static AiSearchService aiSearch;

      public Worker(
         ILogger<Worker> logger,
         IConfiguration configuration,
         StartArgs sArgs,
         SemanticUtilityService semanticUtil,
         DocumentIntelligenceService documentIntel,
         AiSearchService aiSrch)
      {
         Worker.logger = logger;
         config = configuration;
         startArgs = sArgs;
         semanticUtility = semanticUtil;
         documentIntelligence = documentIntel;
         aiSearch = aiSrch;
      }

      protected async override Task ExecuteAsync(CancellationToken stoppingToken)
      {
         Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
         rootParser = CommandBuilder.BuildCommandLine();
         string[] args = startArgs.Args;
         if (args.Length == 0) args = ["-h"];
         _ = await rootParser.InvokeAsync(args);
         bool firstPass = true;
         int fileCount = 0;

         while (true)
         {
            syS.Console.WriteLine();
            if (firstPass && string.IsNullOrWhiteSpace(activeDocument))
            {
               fileCount = await rootParser.InvokeAsync("list");
            }

            if (fileCount > 0)
            {
               if (!string.IsNullOrWhiteSpace(activeDocument))
               {
                  logger.LogInformation(new() { { "Active Document: ", ConsoleColor.DarkGreen }, { activeDocument, ConsoleColor.Blue } });
               }
               else
               {
                  logger.LogInformation("Please use the 'doc' command to set an active document to start asking questions. Use 'list' to show available documents or 'process' to index a new document", ConsoleColor.Yellow);
                  logger.LogInformation("");
               }
            }
            else
            {
               logger.LogInformation("Please use the 'process' command to process your first document.", ConsoleColor.Yellow);
               logger.LogInformation("");
            }

            //prompt for command input and invoke the parser
            syS.Console.Write("dq> ");
            var line = syS.Console.ReadLine();

            if (line == null)
            {
               return;
            }

            _ = await rootParser.InvokeAsync(line);
            firstPass = false;
         }
      }

      internal static async Task AskQuestionAsync(string[] question)
      {
         if (question == null || question.Length == 0)
         {
            return;
         }
         if (string.IsNullOrWhiteSpace(activeDocument))
         {
            //log.LogInformation("Please use the 'doc' command to set an active document to start asking questions.", ConsoleColor.Yellow);
            return;
         }
         string quest = string.Join(" ", question);
         syS.Console.WriteLine("----------------------");
         var docContent = await semanticUtility.SearchForReleventContentAsync(activeDocument, quest);

         if (string.IsNullOrWhiteSpace(docContent))
         {
            logger.LogInformation("No relevant content found in the document for the question. Please verify your document name with the 'list' command or try another question.", ConsoleColor.Yellow);
         }
         else
         {
            await foreach (var bit in semanticUtility.AskQuestionStreamingAsync(quest, docContent))
            {
               syS.Console.Write(bit);
            }
         }

         syS.Console.WriteLine();
         syS.Console.WriteLine("----------------------");
         syS.Console.WriteLine();
      }

      internal static async void AzureOpenAiSettingsAsync(string chatModel, string chatDeployment, string embedModel, string embedDeployment)
      {
         if (string.IsNullOrWhiteSpace(chatModel) && 
            string.IsNullOrWhiteSpace(chatDeployment) && 
            string.IsNullOrWhiteSpace(embedModel) && 
            string.IsNullOrWhiteSpace(embedDeployment))
         {
            await rootParser.InvokeAsync("ai set -h");
            return;
         }

         bool changed = false;

         if (!string.IsNullOrWhiteSpace(chatModel))
         {
            config[Constants.OPENAI_CHAT_MODEL_NAME] = chatModel;
            logger.LogInformation(new() { { "Set chat model to", ConsoleColor.DarkYellow }, { chatModel, ConsoleColor.Yellow } });
            changed = true;
         }

         if (!string.IsNullOrWhiteSpace(chatDeployment))
         {
            config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME] = chatDeployment;
            logger.LogInformation(new() { { "Set chat deployment to", ConsoleColor.DarkYellow }, { chatDeployment, ConsoleColor.Yellow } });
            changed = true;
         }

         if (!string.IsNullOrWhiteSpace(embedModel))
         {
            config[Constants.OPENAI_EMBEDDING_MODEL_NAME] = embedModel;
            logger.LogInformation(new() { { "Set embedding model to", ConsoleColor.DarkYellow }, { embedModel, ConsoleColor.Yellow } });
            changed = true;
         }

         if (!string.IsNullOrWhiteSpace(embedDeployment))
         {
            config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME] = embedDeployment;
            logger.LogInformation(new() { { "Set embedding deployment to", ConsoleColor.DarkYellow }, { embedDeployment, ConsoleColor.Yellow } });
            changed = true;
         }

         if (changed)
         {
            semanticUtility.InitMemoryAndKernel();
            ListAiSettings();
         }
      }

      internal static void ListAiSettings()
      {
         int pad = 21;
         logger.LogInformation("-------------------------------------");
         logger.LogInformation("Azure OpenAI settings", ConsoleColor.Gray);
         logger.LogInformation(new() { { "Chat Model:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_CHAT_MODEL_NAME], ConsoleColor.Blue } });
         logger.LogInformation(new() { { "Chat Deployment:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_CHAT_DEPLOYMENT_NAME], ConsoleColor.Blue } });
         logger.LogInformation(new() { { "Embedding Model:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_EMBEDDING_MODEL_NAME], ConsoleColor.Blue } });
         logger.LogInformation(new() { { "Embedding Deployment:".PadRight(pad, ' '), ConsoleColor.DarkBlue }, { config[Constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME], ConsoleColor.Blue } });
         logger.LogInformation("-------------------------------------");
      }

      internal static async Task<int> ListFilesAsync(object t)
      {
         var names = await aiSearch.ListAvailableIndexesAsync();

         if (names.Count > 0)
         {
            logger.LogInformation("List of available documents:", ConsoleColor.Yellow);
         }

         foreach (var name in names)
         {
            logger.LogInformation(name);
         }

         return names.Count;
      }

      internal static async Task ProcessFileAsync(string[] file)
      {
         if (file.Length == 0)
         {
            logger.LogInformation("Please enter a file name to process", ConsoleColor.Red);
            return;
         }

         string name = string.Join(" ", file);

         if (!File.Exists(name))
         {
            logger.LogInformation($"The file {name} doesn't exist. Please enter a valid file name", ConsoleColor.Red);
            return;
         }

         await documentIntelligence.ProcessDocumentAsync(new FileInfo(name));
      }

      internal static void SetActiveDocument(string[] document)
      {
         var docName = string.Join(" ", document);
         activeDocument = docName;
      }
   }
}
