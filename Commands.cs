﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;
using System.IO;

namespace FattyBot
{
    partial class FattyBot
    {

        #region GoogleStructs
        const string GoogleAPIKey = "AIzaSyDniQGV3voKW5ZSrqWfiXgnWz-2AX6xNBo";
        private class GoogleSearchItem
        {
            public string kind { get; set; }
            public string title { get; set; }
            public string link { get; set; }
            public string displayLink { get; set; }
        }

        private class SourceUrl
        {
            public string type { get; set; }
            public string template { get; set; }
        }

        private class GoogleSearchResults
        {
            public string kind { get; set; }
            public SourceUrl url { get; set; }
            public GoogleSearchItem[] items { get; set; }
        }
        #endregion

        const string WolframAlphaKey = "95JE4A-XQLX9WPU99";

        UserAliasesRegistry FattyUserAliases = new UserAliasesRegistry(); 

        private void ListCommands(string caller, string args, string source)
        {
            StringBuilder availableMethodNames = new StringBuilder();
            foreach (KeyValuePair<string, Tuple<CommandMethod, string>> mthd in Commands)
            {
                availableMethodNames.Append(CommandSymbol+mthd.Key);
                availableMethodNames.Append(":" + mthd.Value.Item2 + ", ");
            }
            availableMethodNames.Remove(availableMethodNames.Length - 2, 1);
            SendMessage(source, availableMethodNames.ToString());
        }

        private void Seen(string caller, string args, string source)
        {
            Tuple<DateTime, String> lastSeentEntry;
            
            bool userSeen = SeenList.TryGetValue(args, out lastSeentEntry);
            
            if (userSeen)
            {
                DateTime lastSeentTime = lastSeentEntry.Item1;
                TimeSpan lastSeenSpan = DateTime.Now - lastSeentTime;
                string prettyTime = GetPrettyTime(lastSeenSpan);
                
                if (caller == args)
                    SendMessage(source, String.Format("You were last seen (before this command) {0} on {1}. {2} ago. \"{3}\"", args, lastSeentTime, prettyTime, lastSeentEntry.Item2));
                else
                    SendMessage(source, String.Format("Last seen {0} on {1}. {2} ago. \"{3}\"", args, lastSeentTime, prettyTime, lastSeentEntry.Item2));

            }
            else
            {
                SendMessage(source, String.Format("Haven't ever seen \"{0}\" around", args));
            }
        }

        private void Tell(string caller, string args, string source)
        {
            var parts = args.Split(' ');
            var recipients = parts[0].Split(',');
            string msg = String.Join(" ", parts, 1, parts.Length-1);
            foreach(var recip in recipients) {
                if (recip == IrcObject.IrcNick) {
                    SendMessage(source, "xD");
                    continue;
                }
                else {
                    FattyTellManager.AddTellForUser(recip, caller, msg);
                }
            }
            SendMessage(source, String.Format("Will tell that to {0} when they are round", parts[0]));
        }

        private void Alias(string caller, string args, string source) {
            args = args.Trim();
            var argParts = args.Split(' ');

            if (argParts.Length == 3)
                PerformAliasOperation(argParts, source);
            else if (argParts.Length == 1)
                DisplayUserAliases(argParts[0], source, args);
            else
                SendMessage(source, "Not the right number of inputs");
        }

        #region EightBall
        readonly string[] EightBallResponses = { "It is certain", "It is decidedly so", "Without a doubt", "Yes definitely", "You may rely on it", "As I see it, yes", 
                             "Most likely", "Outlook good", "Yes", "Signs point to yes", "Reply hazy, try again", "Try again later", "Better not tell you now", 
                             "Cannot predict now", "Concentrate and ask again", "Don't count on it", "My reply is no", "My sources say no", "Outlook not so good", "Very doubtful" };

        private void EightBall(string caller, string args, string source)
        {
            Random rand = new Random();

            SendMessage(source, String.Format("{0}: {1}", caller, EightBallResponses[rand.Next(EightBallResponses.Length)]));
        }


        #endregion

        private void Math(string caller, string args, string source)
        {
            string searchURL = "http://api.wolframalpha.com/v2/query?input=" + args + "&appid=" + WolframAlphaKey;

            HttpWebRequest searchRequest = HttpWebRequest.Create(searchURL) as HttpWebRequest;
            HttpWebResponse searchResponse = searchRequest.GetResponse() as HttpWebResponse;
            StreamReader reader = new StreamReader(searchResponse.GetResponseStream());

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(reader);
            StringBuilder messageAccumulator = new StringBuilder();
            int messageOverhead = GetMessageOverhead(source);


            XmlNodeList res = xmlDoc.GetElementsByTagName("queryresult");
            if (res[0].Attributes["success"].Value == "false")
            {
                messageAccumulator.Append("Query failed: ");
                res = xmlDoc.GetElementsByTagName("tip");
                for (int i = 0; i < res.Count; i++)
                {
                    string desc = res[i].InnerText;
                    if (desc.Length + messageOverhead + messageAccumulator.Length <= 480)
                        messageAccumulator.Append(desc + ". ");
                    else
                        break;
                }
                res = xmlDoc.GetElementsByTagName("didyoumean");
                
                for (int i = 0; i < res.Count; i++)
                {
                    string desc = "";
                    if(i==0)
                        desc += "Did you mean: ";
                    desc += res[i].InnerText + " ? ";
                    if (desc.Length + messageOverhead + messageAccumulator.Length <= 480)
                        messageAccumulator.Append(desc);
                    else
                        break;
                }
                SendMessage(source, messageAccumulator.ToString());
            }
            else
            {
                res = xmlDoc.GetElementsByTagName("plaintext");

                for (int i = 0; i < res.Count; i++)
                {
                    string value = res[i].InnerText;
                    string description = res[i].ParentNode.ParentNode.Attributes["title"].Value;
                    if(description == "Number line")
                        continue;
                    description = description + ":" + value;
                    if (description.Length + messageOverhead + messageAccumulator.Length <= 480)
                        messageAccumulator.Append(description + " | ");
                    else
                        break;
                }

                messageAccumulator.Replace("\n", " ");
                messageAccumulator.Replace("\r", " ");

                SendMessage(source, messageAccumulator.ToString());
            }

            
                
        }

        private void DisplayUserAliases(string alias, string source, string args) {
            StringBuilder sb = new StringBuilder();
            UserAliasGroup ag;
            if (FattyUserAliases.GetAliasGroup(alias, out ag)) {
                var names = ag.GetUserAliases();
                foreach (string name in names) {
                    sb.Append(name + " ");
                }
                SendMessage(source, sb.ToString());
            }
            else {
                SendMessage(source, String.Format("No results found for {0}", args));
            }
        }

        private void PerformAliasOperation(string[] argParts, string source) {
            if (argParts.Length < 3)
                SendMessage(source, "error parsing arguments");
            string operation = argParts[0];
            string firstName = argParts[1];
            string secondName = argParts[2];
            switch (operation) {
                case "add":
                    SendMessage(source, FattyUserAliases.AddAlias(firstName, secondName));
                    break;
                case "remove":
                    SendMessage(source, FattyUserAliases.RemoveAlias(firstName, secondName));
                    break;
                default:
                    SendMessage(source, String.Format("{0} is an unknown operation, try 'add' or 'remove'", operation));
                    break;
            }
        }

        private void Google(string caller, string args, string source)
        {
            string searchURL = "https://www.googleapis.com/customsearch/v1?key=" + GoogleAPIKey + "&cx=016968405084681006944:ksw5ydltpt0&q=";
            searchURL += args;
            GoogleAPIPrinter(searchURL, source);
        }

        private void GoogleImageSearch(String caller, String args, string source)
        {
            string searchURL = "https://www.googleapis.com/customsearch/v1?key=" + GoogleAPIKey + "&cx=016968405084681006944:ksw5ydltpt0&searchType=image&q=";
            searchURL += args;
            GoogleAPIPrinter(searchURL, source);
        }

        private void GoogleAPIPrinter(string searchURL, string source)
        {
            HttpWebRequest searchRequest = HttpWebRequest.Create(searchURL) as HttpWebRequest;
            HttpWebResponse searchResponse = searchRequest.GetResponse() as HttpWebResponse;
            StreamReader reader = new StreamReader(searchResponse.GetResponseStream());
            String data = reader.ReadToEnd();

            GoogleSearchResults results = JsonConvert.DeserializeObject<GoogleSearchResults>(data);
            StringBuilder messageAccumulator = new StringBuilder();
            int i = 0;
            int messageOverhead = GetMessageOverhead(source);
            while (i < 10)
            {
                if (results.items != null && results.items.Length >= i)
                {
                    StringBuilder resultAccumulator = new StringBuilder();
                    GoogleSearchItem resultIterator = results.items[i++];
                    resultAccumulator.Append(String.Format("\"{0}\"", resultIterator.title));
                    resultAccumulator.Append(" - ");
                    resultAccumulator.Append(String.Format("\x02{0}\x02", resultIterator.link));
                    resultAccumulator.Append(@" 4| ");
                    if (messageAccumulator.Length + resultAccumulator.Length + messageOverhead <= 480)
                        messageAccumulator.Append(resultAccumulator);
                    else
                        break;
                }
                else
                {
                    break;
                }
            }
            if (messageAccumulator.Length == 0)
                SendMessage(source, "No Results Found");
            else
                SendMessage(source, messageAccumulator.ToString());
        }
                
        private string GetPrettyTime(TimeSpan ts)
        {
            string timeLastSeen = "";
            int fieldCount = 0;
            if (ts.Days > 0)
            {
                timeLastSeen += String.Format("{0} day(s)", ts.Days);
                ++fieldCount;
            }
            if (ts.Hours > 0)
            {
                timeLastSeen += (fieldCount > 0 ? ", " : "");
                timeLastSeen += String.Format("{0} hour(s)", ts.Hours);
                ++fieldCount;
            }
            if (ts.Minutes > 0)
            {
                timeLastSeen += (fieldCount > 0 ? ", and " : "");
                timeLastSeen += String.Format("{0} minute(s)", ts.Minutes);
                ++fieldCount;
            }
            if(fieldCount == 0)
            {
                timeLastSeen = String.Format("{0} second(s)", ts.Seconds);
            }

            return timeLastSeen;
        }

        private int GetMessageOverhead( string source)
        {
            return  String.Format("PRIVMSG {0} :", source).Length;
        }

    }
}