﻿using System.Linq;
using System.Text.RegularExpressions;
using monitorbot.core.bot;
using monitorbot.core.compareengine;
using monitorbot.core.persistence;
using monitorbot.teamcity.services;
using monitorbot.core.utils;

namespace monitorbot.teamcity
{
    public class TeamcityBuildTracker : IMessageProcessor
    {
        private readonly ICommandParser m_CommandParser;
        private readonly ListPersistenceApi<Tracked<TeamcityBuildStatus>> m_Persistence;
        private readonly ITeamcityBuildApi m_TeamcityBuildApi;
        private static readonly Regex s_TeamcityBuildRegex = new Regex(@"^build#(?<id>\d{5,10})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly CompareEngine<TeamcityBuildStatus> m_TeamcityBuildCompareEngine;

        public TeamcityBuildTracker(ICommandParser commandParser, IKeyValueStore persistence, ITeamcityBuildApi teamcityBuildApi)
        {
            m_CommandParser = commandParser;
            m_Persistence = new ListPersistenceApi<Tracked<TeamcityBuildStatus>>(persistence, "tracked-tc-builds");
            m_TeamcityBuildApi = teamcityBuildApi;
            m_TeamcityBuildCompareEngine = new CompareEngine<TeamcityBuildStatus>(x => string.Format("<http://teamcity/viewLog.html?buildId={0}|Build {0}> ({1}) updated:", x.Id, x.Name),
                new[] { new PropertyComparer<TeamcityBuildStatus>(x => x.OldValue.State != x.NewValue.State, FormatStateChanged)});
        }

        private Response FormatStateChanged(Update<TeamcityBuildStatus> x)
        {
            if (x.NewValue.State == BuildState.Succeeded)
            {
                return new Response("build finished", null);
            }

            return new Response(string.Format("build state changed from {0} to {1}", x.OldValue.State, x.NewValue.State), null);
        } 

        public MessageResult ProcessTimerTick()
        {
            var trackedTickets = m_Persistence.ReadList();

            var comparison = trackedTickets.Select(x =>
                new Update<TeamcityBuildStatus>(x.Channel, x.Value, m_TeamcityBuildApi.GetBuild(x.Value.Id).Result)
            ).Where(x => x.NewValue.IsNotDefault());

            var results = m_TeamcityBuildCompareEngine.Compare(comparison).ToList();

            foreach (var result in results)
            {
                var id = result.NewValue.Id;
                m_Persistence.RemoveFromList(x => x.Value.Id == id);
                m_Persistence.AddToList(new Tracked<TeamcityBuildStatus>(result.NewValue, result.Response.Channel));
            }

            return new MessageResult(results.Select(x => x.Response).ToList());
        }

        public MessageResult ProcessMessage(Message message)
        {
            string toTrack;
            if (m_CommandParser.TryGetCommand(message, "track", out toTrack) && s_TeamcityBuildRegex.IsMatch(toTrack))
            {
                var build = m_TeamcityBuildApi.GetBuild(toTrack.Substring(6)).Result;
                if (build.Equals(TeamcityBuildStatus.Unknown)) return Response.ToMessage(message, FormatNotFoundMessage(toTrack));
                m_Persistence.AddToList(new Tracked<TeamcityBuildStatus>(build, message.Channel));
                return Response.ToMessage(message, FormatNowTrackingMessage(toTrack));
            }
            return MessageResult.Empty;
        }

        private string FormatNotFoundMessage(string toTrack)
        {
            return string.Format("Could not find {0}. It might not have started, or might not be tracked by the api", toTrack);
        }

        private static string FormatNowTrackingMessage(string toTrack)
        {
            return string.Format("Now tracking {0}. To stop tracking, use `untrack {0}`", toTrack);
        }
    }
}
