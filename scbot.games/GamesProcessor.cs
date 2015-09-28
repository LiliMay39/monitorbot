﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PlayerRank;
using PlayerRank.Scoring.Elo;
using scbot.core.bot;
using scbot.core.persistence;
using scbot.core.utils;

namespace scbot.games
{
    public class GamesProcessor : ICommandProcessor
    {
        public static IFeature Create(ICommandParser commandParser, IKeyValueStore persistence)
        {
            var processor = new GamesProcessor(persistence);
            return new BasicFeature("games", "record games and track rankings", 
                "Use `record <league> game 1st <player1> 2nd <player2> [...]` to record a game.\n" +
                "eg: `record worms game 1st James 2nd Luke 3rd MarkJ`\n" +
                "Use `get <league> leaderboard to see overall ratings",
                new HandlesCommands(commandParser, processor));
        }

        private readonly IKeyValueStore m_Persistence;
        private readonly RegexCommandMessageProcessor m_Underlying;
        private readonly EloScoringStrategy m_EloScoringStrategy = new EloScoringStrategy(maxRatingChange: new Points(64), maxSkillGap: new Points(400), startingRating: new Points(s_StartingRating));
        private static readonly int s_StartingRating = 1000;

        public GamesProcessor(IKeyValueStore persistence)
        {
            m_Persistence = persistence;
            m_Underlying = new RegexCommandMessageProcessor(Commands);
        }

        public Dictionary<string, MessageHandler> Commands
        {
            get
            {
                return new Dictionary<string, MessageHandler>
                {
                    {@"record\s+(?<league>[^ ]+)\s+game\s*(?<results>.+)?", RecordGame},
                    {@"(?<league>[^ ]+)\s+leaderboard", GetLeaderboard},
                };
            }
        }

        private MessageResult RecordGame(Command command, Match args)
        {
            // TODO: try and break up this method a bit more
            var resultsString = args.Group("results");
            var gameResults = ParseGameResults(resultsString);
            if (!gameResults.Any())
            {
                if (String.IsNullOrWhiteSpace(resultsString))
                {
                    return Response.ToMessage(command, "Please provide some game results");
                }
                return Response.ToMessage(command, string.Format("Could not parse results `{0}`", resultsString));
            }
            var responses = new List<Response>();

            var leagueName = args.Group("league");
            var gamesPersistence = new ListPersistenceApi<Game>(m_Persistence, "games." + leagueName);
            var existingGames = gamesPersistence.ReadList();
            var league = GetCurrentLeague(existingGames);
            var oldLeaderboard = league.GetLeaderBoard(m_EloScoringStrategy);

            var playersPersistence = new HashPersistenceApi<int>(m_Persistence, "players." + leagueName);
            var playerNames = playersPersistence.GetKeys();
            if (!existingGames.Any())
            {
                responses.Add(Response.ToMessage(command, string.Format("Creating new league `{0}`", leagueName)));
            }

            gameResults = GetCanonicalPlayerNames(gameResults, playerNames);

            var newPlayers = FindNewPlayers(gameResults, playerNames);
            responses.AddRange(newPlayers.Select(x => Response.ToMessage(command, string.Format("Adding new player `{0}`", x))));

            var newGame = new Game(gameResults);
            gamesPersistence.AddToList(newGame);
            league.RecordGame(GetPlayerRankGame(newGame));

            var newLeaderboard = league.GetLeaderBoard(m_EloScoringStrategy);
            foreach (var player in newGame.Results.Select(x => x.Player))
            {
                var newRanking = GetRatingForPlayer(newLeaderboard.ToList(), player);
                playersPersistence.Set(player, int.Parse(newRanking.ToString()));
            }

            var rankingChanges = GetResultsWithRankingChanges(oldLeaderboard.ToList(), newLeaderboard.ToList(), newGame);
            responses.AddRange(rankingChanges.Select(x => Response.ToMessage(command, x)));
            return new MessageResult(responses);
        }

        private List<PlayerPosition> GetCanonicalPlayerNames(List<PlayerPosition> gameResults, List<string> playerNames)
        {
            var ciPlayerNames = playerNames.ToDictionary(x => x, x => x, StringComparer.InvariantCultureIgnoreCase);
            return gameResults.Select(x => new PlayerPosition(GetCanonicalPlayerName(ciPlayerNames, x.Player), x.Position)).ToList();
        }

        private string GetCanonicalPlayerName(Dictionary<string, string> ciPlayerNames, string player)
        {
            return ciPlayerNames.ContainsKey(player) ? ciPlayerNames[player] : player;
        }

        private static List<string> FindNewPlayers(List<PlayerPosition> gameResults, List<string> playerNames)
        {
            var newPlayers = new List<string>();
            foreach (var result in gameResults)
            {
                if (!playerNames.Contains(result.Player))
                {
                    var newPlayer = result.Player;
                    newPlayers.Add(newPlayer);
                }
            }

            return newPlayers;
        }

        private static League GetCurrentLeague(List<Game> existingGames)
        {
            var prLeague = new PlayerRank.League();
            foreach (var existingGame in existingGames)
            {
                var leagueGame = GetPlayerRankGame(existingGame);
                prLeague.RecordGame(leagueGame);
            }

            return prLeague;
        }

        private MessageResult GetLeaderboard(Command command, Match args)
        {
            var responses = new List<Response>();
            var league = args.Group("league");
            var playersPersistence = new HashPersistenceApi<int>(m_Persistence, "players." + league);
            var position = 0;
            var players = playersPersistence.GetKeys();
            if (!players.Any())
            {
                return Response.ToMessage(command, string.Format("No games found for league `{0}`", league));
            }
            foreach (var playerRating in players
                    .Select(x => new PlayerRating(x, playersPersistence.Get(x)))
                    .OrderByDescending(x => x.Rating))
            {
                position++;
                responses.Add(Response.ToMessage(command, string.Format("{0}: *{1}* (rating {2})", position, playerRating.Name, playerRating.Rating)));
            }
            return new MessageResult(responses);
        }

        private List<string> GetResultsWithRankingChanges(List<PlayerScore> oldRankings, List<PlayerScore> newRankings, Game newGame)
        {
            return newGame.Results.Select(
                x => new {
                        result = x,
                        oldRating = GetRatingForPlayer(oldRankings, x.Player),
                        newRating = GetRatingForPlayer(newRankings, x.Player)
                    })
                .Select(x => string.Format("{0}: *{1}* (new rating - {2})", x.result.Position, x.result.Player, x.newRating))
                .ToList();
        }

        private Points GetRatingForPlayer(List<PlayerScore> rankings, string player)
        {
            var ranking = rankings.FirstOrDefault(x => x.Name == player);
            return ranking != null ? ranking.Points : new Points(s_StartingRating);
        }

        private static PlayerRank.Game GetPlayerRankGame(Game existingGame)
        {
            var leagueGame = new PlayerRank.Game();
            foreach (var result in existingGame.Results)
            {
                // PlayerRank assumes that higher is better for scoring
                leagueGame.AddResult(result.Player, new Position(result.Position));
            }
            return leagueGame;
        }

        private List<PlayerPosition> ParseGameResults(string input)
        {
            var resultsRegex = new Regex(@"(?<resultString>(?<position>\d+)(st|nd|rd|th|:)\s+(?<player>[^\d]+))");
            return resultsRegex.Matches(input)
                .Cast<Match>()
                .Select(result =>
                {
                    var player = result.Group("player").Trim();
                    int position = Int32.Parse(result.Group("position"));
                    return new PlayerPosition(player, position);
                }).ToList();
        }

        public MessageResult ProcessTimerTick()
        {
            return m_Underlying.ProcessTimerTick();
        }

        public MessageResult ProcessCommand(Command command)
        {
            return m_Underlying.ProcessCommand(command);
        }
    }

    internal struct PlayerRating
    {
        public readonly string Name;
        public readonly int Rating;

        public PlayerRating(string name, int rating)
        {
            Name = name;
            Rating = rating;
        }
    }

    internal struct PlayerPosition
    {
        public readonly string Player;
        public readonly int Position;

        public PlayerPosition(string player, int position)
        {
            Player = player;
            Position = position;
        }
    }

    internal struct Game
    {
        public readonly List<PlayerPosition> Results;

        public Game(List<PlayerPosition> results)
        {
            Results = results;
        }
    }
}