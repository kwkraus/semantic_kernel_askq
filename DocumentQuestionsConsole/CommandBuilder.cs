using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;

namespace DocumentQuestions.Console
{
   internal class CommandBuilder
   {
      public static Parser BuildCommandLine()
      {
         // Define the commands and options
         // Command and handler for setting the active document
         var documentArg = new Argument<string[]>("document", "Document to set as active") { Arity = ArgumentArity.ZeroOrMore };
         var docCommand = new Command("doc", "Set the active document to start asking questions")
         {
            documentArg
         };
         docCommand.Handler = CommandHandler.Create<string[]>(Worker.SetActiveDocument);

         // command and handler for asking questions
         var questionArg = new Argument<string[]>("question", "Question to ask about the document") { Arity = ArgumentArity.ZeroOrMore };
         var askQuestionCommand = new Command("ask", "Ask a question on the document(s)")
         {
            questionArg
         };
         askQuestionCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestionAsync);

         // command and handler for processing a file
         var fileArg = new Argument<string[]>("file", "Path to the file to process and index") { Arity = ArgumentArity.ZeroOrMore };
         var uploadCommand = new Command("process", "Process the file contents against Document Intelligence and add to Azure AI Search index")
         {
            fileArg
         };
         uploadCommand.Handler = CommandHandler.Create<string[]>(Worker.ProcessFileAsync);

         // command and handler for listing available files
         var listCommand = new Command("list", "List the available files to ask questions about")
         {
            Handler = CommandHandler.Create(Worker.ListFilesAsync)
         };
         
         // define root command and its symbols
         RootCommand rootCommand = new(description: $"Utility to ask questions on documents that have been indexed in Azure AI Search")
         {
            questionArg,
            docCommand,
            askQuestionCommand,
            uploadCommand,
            listCommand,
            AIRuntimeSetCommand()
         };
         rootCommand.Handler = CommandHandler.Create<string[]>(Worker.AskQuestionAsync);

         var parser = new CommandLineBuilder(rootCommand)
              .UseDefaults()
              .UseHelp(ctx =>
              {
                 ctx.HelpBuilder
                     .CustomizeLayout(_ => HelpBuilder.Default
                        .GetLayout()
                        .Prepend(
                              _ => AnsiConsole.Write(new FigletText("Ask Document Questions"))
                     ));

              })
              .Build();

         return parser;
      }

      private static Command AIRuntimeSetCommand()
      {
         var chatModelOpt = new Option<string>(["--chat-model", "--cm"], "Name of GPT chat model to use (must match model associated with chat deployment)");
         var chatDepoymentOpt = new Option<string>(["--chat-deployment", "--cd"], "Name of GPT chat deployment to use");

         var embedModelOpt = new Option<string>(["--embed-model", "--em"], "Name of model to use for text embedding (must match model associated with embedding deployment)");
         var embedDepoymentOpt = new Option<string>(["--embed-deployment", "--ed"], "Name of text embedding deployment to use");

         var listAICmd = new Command("list", "List the configured Azure OpenAI settings")
         {
            Handler = CommandHandler.Create(Worker.ListAiSettings)
         };

         var aiSetCmd = new Command("set", "Change the Azure OpenAI model and deployment runtime settings")
         {
            listAICmd,
            chatModelOpt,
            chatDepoymentOpt,
            embedModelOpt,
            embedDepoymentOpt
         };

         aiSetCmd.Handler = CommandHandler.Create<string, string, string, string>(Worker.AzureOpenAiSettingsAsync);

         var aiCmd = new Command("ai", "Change or List Azure OpenAI model and deployment runtime settings")
         {
            aiSetCmd,
            listAICmd
         };

         return aiCmd;
      }
   }
}
