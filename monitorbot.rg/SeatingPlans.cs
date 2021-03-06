﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using monitorbot.core.bot;
using monitorbot.core.utils;

namespace monitorbot.rg
{
    public class SeatingPlans : ICommandProcessor
    {
        public static IFeature Create(ICommandParser commandParser, IWebClient webClient)
        {
            return new BasicFeature("seatingplans", 
                "find people/rooms in the building", "use `where is <search>` to search for a person or room", 
                new HandlesCommands(commandParser, new SeatingPlans(webClient)));
        }

        private const string c_RecordReplayBase = @"\\sqlcomparetestdata.red-gate.com\sqlcomparetestdata\RecordReplay\";
        private readonly RegexCommandMessageProcessor m_Underlying;
        private readonly IWebClient m_WebClient;

        public SeatingPlans(IWebClient webClient)
        {
            m_WebClient = webClient;
            m_Underlying = new RegexCommandMessageProcessor(Commands);
        }

        public Dictionary<string, MessageHandler> Commands
        {
            get
            {
                return new Dictionary<string, MessageHandler>
                {
                    { "where is (?<thing>.+)", WhereIs },
                    //{ new Regex(@"init[^ ]* traces for (?<branch>[^ ]+)"), InitTracesFor },
                };
            }
        }

        private MessageResult WhereIs(Command message, Match args)
        {
            var thing = args.Group("thing");
            var results = m_WebClient.DownloadJson("http://seatingplans.red-gate.com/index.php?search_text="
                + HttpUtility.HtmlEncode(thing)).Result;
            var messageResult = new List<Response>();
            foreach (var floor in results)
            {
                var actualFloor = GetFloorLink(floor.table_no);
                var floorName = GetFloorName(floor.table_no);
                var names = GetNested(floor.result);
                messageResult.Add(Response.ToMessage(message, string.Format("<http://seatingplans.red-gate.com/{0}/|{1}>: {2}",
                    actualFloor, floorName, String.Join(", ", names))));
            }
            if (messageResult.Any())
            {
                return new MessageResult(messageResult);
            }
            return Response.ToMessage(message, "No results found for \"" + thing + "\"");
        }

        private IEnumerable<string> GetNested(dynamic result)
        {
            // TODO: result.SelectMany() doesn't work here - need to figure out why
            foreach (var x in result)
                foreach (var y in x)
                    yield return y;
        }

        private string GetFloorLink(int table_no)
        {
            switch (table_no)
            {
                case 0: return "floor2";
                case 1: return "floor1";
                case 2: return "gfloor";
                default: return null;
            }
        }

        private string GetFloorName(int table_no)
        {
            switch (table_no)
            {
                case 0: return "Floor 2";
                case 1: return "Floor 1";
                case 2: return "Ground Floor";
                default: return null;
            }
        }

        public MessageResult ProcessCommand(Command message)
        {
            return m_Underlying.ProcessCommand(message);
        }

        public MessageResult ProcessTimerTick()
        {
            return m_Underlying.ProcessTimerTick();
        }
    }
}
