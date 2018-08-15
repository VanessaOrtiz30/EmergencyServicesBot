using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.CognitiveServices.QnAMaker;
using Microsoft.Bot.Connector;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Reflection;
using System.Xml.Linq;
using System.Configuration;
using System.Net.Http;
using System.Web;

namespace EmergencyServicesBot.Dialogs
{
    [Serializable]
    public class RootDialog : IDialog<object>
    {
        static ResourceManager translateDialog = new ResourceManager("EmergencyServicesBot.Resources.Resources", Assembly.GetExecutingAssembly());

        //Set the language to be used; you can change this on-demand to change the langauage across the app
        //You will pass this everytime you request a value from the resx file
        static CultureInfo ciEnglish = new CultureInfo("en-US");
        static CultureInfo ciSpanish = new CultureInfo("es-US");
        static CultureInfo ciChinese = new CultureInfo("zh-CN");

        public async Task StartAsync(IDialogContext context)
        {
            /* Wait until the first message is received from the conversation and call MessageReceviedAsync 
            *  to process that message. */
            context.Wait(this.MessageReceivedAsync);
        }

        private async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            /* When MessageReceivedAsync is called, it's passed an IAwaitable<IMessageActivity>. To get the message,
            *  await the result. */
            var message = await result;

            var qnaSubscriptionKey = ConfigurationManager.AppSettings[@"QnASubscriptionKey"];
            var qnaKBId = ConfigurationManager.AppSettings[@"QnAKnowledgebaseId"];
            var translatorKey = ConfigurationManager.AppSettings[@"TranslatorApiKey"];


            // QnA Subscription Key, KnowledgeBase Id, and TranslatorApiKey null verification
            if (string.IsNullOrEmpty(qnaSubscriptionKey) || string.IsNullOrEmpty(qnaKBId) || string.IsNullOrEmpty(translatorKey))
            {
                await context.PostAsync("Please set QnAKnowledgebaseId, QnASubscriptionKey, and TranslatorApiKey in App Settings. Get them at https://qnamaker.ai and https://www.microsoft.com/en-us/translator/.");
            }
            else
            {
                await DetectAndSaveUserLanguageAsync(context, message.Text);

                string[] choices = GetMainMenuChoices(context);

                // detect language and set appropriate cultureInfo
                var welcomeMessage = translateDialog.GetString("Welcome", context.UserData.GetValue<CultureInfo>("cultureInfo"));

                PromptDialog.Choice(context, UserChoiceMade, choices, welcomeMessage);
            }
            
        }

        private static string[] GetMainMenuChoices(IDialogContext context)
        {
            string[] choices = new[] { translateDialog.GetString("GetAnswers", context.UserData.GetValue<CultureInfo>("cultureInfo")), translateDialog.GetString("SetLanguage", context.UserData.GetValue<CultureInfo>("cultureInfo")) };
            if (context.Activity.ChannelId == ChannelIds.Sms)
            {   // on SMS, communicate they can choose by replying with "1" or "2"
                choices = new[] { translateDialog.GetString("mobileGetAnswers", context.UserData.GetValue<CultureInfo>("cultureInfo")), translateDialog.GetString("mobileSetLanguage", context.UserData.GetValue<CultureInfo>("cultureInfo")) };
            }

            return choices;
        }

        private async Task DetectAndSaveUserLanguageAsync(IDialogContext context, string userText)
        {
            
            using (var langDetectClient = new HttpClient())
            {
                langDetectClient.DefaultRequestHeaders.Add(@"Ocp-Apim-Subscription-Key", ConfigurationManager.AppSettings[@"TranslatorApiKey"]);

                try
                {
                    var detectedLanguageXmlResponse = XDocument.Parse(await langDetectClient.GetStringAsync($@"{ConfigurationManager.AppSettings[@"TranslatorEndpoint"]}/Detect?text={HttpUtility.UrlEncode(userText)}"));

                    context.UserData.SetValue(@"userLanguage", detectedLanguageXmlResponse.Root.Value);

                    if (detectedLanguageXmlResponse.Root.Value == "en")
                    {
                        context.UserData.SetValue(@"cultureInfo", ciEnglish);
                    }
                    else if (detectedLanguageXmlResponse.Root.Value == "es")
                    {
                        context.UserData.SetValue(@"cultureInfo", ciSpanish);
                    }
                    else if (detectedLanguageXmlResponse.Root.Value == "zh-CN")
                    {
                        context.UserData.SetValue(@"cultureInfo", ciChinese);
                    }
                    else { context.UserData.SetValue(@"cultureInfo", ciEnglish); }


                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($@"Error detecting language from inital user chat: {ex.ToString()}");
                }
            }

                if (context.UserData.GetValue<String>("userLanguage") == "en")
                {
                    context.UserData.SetValue(@"cultureInfo", ciEnglish);
                }
                else if (context.UserData.GetValue<String>("userLanguage") == "es")
                {
                    context.UserData.SetValue(@"cultureInfo", ciSpanish);
                }
                else if (context.UserData.GetValue<String>("userLanguage") == "zh-CN")
                {
                    context.UserData.SetValue(@"cultureInfo", ciChinese);
                }
                else { context.UserData.SetValue(@"cultureInfo", ciEnglish); }
            
        }

        private async Task UserChoiceMade(IDialogContext context, IAwaitable<string> result)
        {
            var choice = await result;

            if ((choice.IndexOf(@"get answers", 0, StringComparison.OrdinalIgnoreCase) != -1) ||
                (choice.IndexOf(@"Obtener Respuestas", 0, StringComparison.OrdinalIgnoreCase) != -1) ||
                (choice.IndexOf(@"其他问题", 0, StringComparison.OrdinalIgnoreCase) != -1))
                context.Call(new QandADialog(), DoneWithSubdialog);
            else if ((choice.IndexOf(@"Select language", 0, StringComparison.OrdinalIgnoreCase) != -1) ||
                (choice.IndexOf(@"Seleccione el idioma", 0, StringComparison.OrdinalIgnoreCase) != -1) ||
                (choice.IndexOf(@"选择语言", 0, StringComparison.OrdinalIgnoreCase) != -1))
                context.Call(new SetLanguage(), DoneWithSubdialog);
        }

        private Task DoneWithSubdialog(IDialogContext context, IAwaitable<object> result)
        {
            var choices = GetMainMenuChoices(context);

            // detect language and set appropriate cultureInfo
            var message = translateDialog.GetString("NewQuestion", context.UserData.GetValue<CultureInfo>("cultureInfo"));

            PromptDialog.Choice(context, UserChoiceMade, choices, message);

            return Task.CompletedTask;
        }

        private async Task AfterAnswerAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            // wait for the next user message
            context.Wait(MessageReceivedAsync);
        }

        //This method is only called once and always uses English Language Resources
        public static async Task SendWelcomeMessage(IMessageActivity activity)
        {

            ConnectorClient client = new ConnectorClient(new Uri(activity.ServiceUrl));

            var title = translateDialog.GetString("WelcomeTitle", ciEnglish);
        
            IList<Attachment> cardsAttachment = new List<Attachment>();
            var reply = ((Activity)activity).CreateReply();
            
            CardImage CI = new CardImage
            {
                Url = translateDialog.GetString("WelcomeImageUrl", ciEnglish),
            };


            var heroCard = new HeroCard
            {
                Title = title,
                Subtitle = "Hello. Hola. 你好.",
                Text = "Say \"hi\" to begin, diga \"hola\" para comenzar, 说“嗨”开始",
                Images = new List<CardImage> { CI }
            };

           
            cardsAttachment.Add(heroCard.ToAttachment());

            reply.Attachments = cardsAttachment;

            await client.Conversations.ReplyToActivityAsync(reply);

        }
    }

    // For more information about this template visit http://aka.ms/azurebots-csharp-qnamaker
    [Serializable]
    public class BasicQnAMakerDialog : QnAMakerDialog
    {
        // Go to https://qnamaker.ai and feed data, train & publish your QnA Knowledgebase.        
        // Parameters to QnAMakerService are:
        // Required: subscriptionKey, knowledgebaseId, 
        // Optional: defaultMessage, scoreThreshold[Range 0.0 – 1.0]
        public BasicQnAMakerDialog() : base(new QnAMakerService(new QnAMakerAttribute(Utils.GetAppSetting("QnASubscriptionKey"), Utils.GetAppSetting("QnAKnowledgebaseId"), "I could not find an answer to your question. Please try again or contact Houston 311.", 0.5)))
        {}
    }
}