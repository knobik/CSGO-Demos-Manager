﻿using CSGO_Demos_Manager.Models;
using CSGO_Demos_Manager.Models.Events;
using DemoInfo;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MoreLinq;

namespace CSGO_Demos_Manager.Services.Analyzer
{
	public class ValveAnalyzer : DemoAnalyzer
	{
		public ValveAnalyzer(Demo demo)
		{
			Parser = new DemoParser(File.OpenRead(demo.Path));
			// Reset to have update on UI
			demo.ResetStats();
			Demo = demo;
			RegisterEvents();
		}

		protected sealed override void RegisterEvents()
		{
			Parser.MatchStarted += HandleMatchStarted;
			Parser.RoundAnnounceMatchStarted += HandleRoundAnnounceMatchStarted;
			Parser.RoundMVP += HandleRoundMvp;
			Parser.PlayerKilled += HandlePlayerKilled;
			Parser.RoundStart += HandleRoundStart;
			Parser.RoundOfficiallyEnd += HandleRoundOfficiallyEnd;
			Parser.BombPlanted += HandleBombPlanted;
			Parser.BombDefused += HandleBombDefused;
			Parser.BombExploded += HandleBombExploded;
			Parser.TickDone += HandleTickDone;
			Parser.WeaponFired += HandleWeaponFired;
			Parser.RoundEnd += HandleRoundEnd;
			Parser.FlashNadeExploded += HandleFlashNadeExploded;
			Parser.ExplosiveNadeExploded += HandleExplosiveNadeExploded;
			Parser.SmokeNadeStarted += HandleSmokeNadeStarted;
			Parser.FireNadeEnded += HandleFireNadeEnded;
			Parser.BotTakeOver += HandleBotTakeOver;
			Parser.LastRoundHalf += HandleLastRoundHalf;
			Parser.WinPanelMatch += HandleWinPanelMatch;
			Parser.SmokeNadeEnded += HandleSmokeNadeEnded;
			Parser.FireNadeStarted += HandleFireNadeStarted;
			Parser.DecoyNadeStarted += HandleDecoyNadeStarted;
			Parser.DecoyNadeEnded += HandleDecoyNadeEnded;
			Parser.PlayerHurt += HandlePlayerHurted;
			Parser.RankUpdate += HandleServerRankUpdate;
			Parser.PlayerDisconnect += HandlePlayerDisconnect;
		}

		public override async Task<Demo> AnalyzeDemoAsync(CancellationToken token)
		{
			Parser.ParseHeader();

			await Task.Run(() => Parser.ParseToEnd(token), token);

			Application.Current.Dispatcher.Invoke(delegate
			{
				UpdateKillsCount();
				UpdatePlayerScore();
				Demo.Rounds.Add(CurrentRound);
				if (Demo.Players.Any())
				{
					Demo.MostHeadshotPlayer = Demo.Players.OrderByDescending(x => x.HeadshotPercent).First();
					Demo.MostBombPlantedPlayer = Demo.Players.OrderByDescending(x => x.BombPlantedCount).First();
					Demo.MostEntryKillPlayer = Demo.Players.MaxBy(p => p.EntryKills.Count);
				}
				if (Demo.Kills.Any())
				{
					var weapons = Demo.Kills.GroupBy(k => k.Weapon).Select(weap => new
					{
						Weapon = weap.Key,
						Count = weap.Count()
					}).OrderByDescending(w => w.Count);
					if(weapons.Any()) Demo.MostKillingWeapon = weapons.Select(w => w.Weapon).First();
				}

				if (AnalyzePlayersPosition)
				{
					LastPlayersFireEndedMolotov.Clear();
				}
			});

			return Demo;
		}

		#region Events Handlers

		private void HandleServerRankUpdate(object sender, RankUpdateEventArgs e)
		{
			PlayerExtended player = Demo.Players.FirstOrDefault(p => p.SteamId == e.SteamId);
			if (player != null)
			{
				player.RankNumberOld = e.RankOld;
				player.RankNumberNew = e.RankNew;
				player.WinCount = e.WinCount;
			}
		}

		private void HandleBotTakeOver(object sender, BotTakeOverEventArgs e)
		{
			if (!IsMatchStarted) return;
			PlayerExtended player = Demo.Players.FirstOrDefault(p => p.SteamId == e.Taker.SteamID);
			if (player != null) player.IsControllingBot = true;
		}

		protected void HandleLastRoundHalf(object sender, LastRoundHalfEventArgs e)
		{
			if (!IsMatchStarted) return;
			IsLastRoundHalf = true;
		}

		protected override void HandleMatchStarted(object sender, MatchStartedEventArgs e)
		{
			if (IsMatchStarted) Demo.ResetStats(false);
			StartMatch();
		}

		protected void HandleRoundAnnounceMatchStarted(object sender, RoundAnnounceMatchStartedEventArgs e)
		{
			if (!IsMatchStarted) StartMatch();
		}

		protected override void HandleRoundStart(object sender, RoundStartedEventArgs e)
		{
			if (!IsMatchStarted) return;
			// Check players count to prevent missing players who was connected after the match started event
			if (Demo.Players.Count < 10)
			{
				// Add all players to our ObservableCollection of PlayerExtended
				foreach (Player player in Parser.PlayingParticipants)
				{
					// don't add bot and already known players
					if (player.SteamID != 0 && Demo.Players.FirstOrDefault(p => p.SteamId == player.SteamID) == null)
					{
						PlayerExtended pl = new PlayerExtended
						{
							SteamId = player.SteamID,
							Name = player.Name,
							Side = player.Team
						};
						Application.Current.Dispatcher.Invoke(delegate
						{
							Demo.Players.Add(pl);
							pl.TeamName = pl.Side == Team.CounterTerrorist ? Demo.TeamCT.Name : Demo.TeamT.Name;
						});
					}
				}
			}
			CreateNewRound();
		}

		protected new void HandleRoundEnd(object sender, RoundEndedEventArgs e)
		{
			base.HandleRoundEnd(sender, e);

			if (IsLastRoundHalf)
			{
				IsSwapTeamRequired = true;
				Application.Current.Dispatcher.Invoke(() => Demo.Rounds.Add(CurrentRound));
			}

			if (IsOvertime && IsLastRoundHalf) IsHalfMatch = false;
		}

		protected void HandleWinPanelMatch(object sender, WinPanelMatchEventArgs e)
		{
			if (!IsMatchStarted) return;

			if (IsOvertime)
			{
				Application.Current.Dispatcher.Invoke(delegate
				{
					Demo.Overtimes.Add(CurrentOvertime);
				});
			}

			ProcessPlayersRating();
			Demo.Winner = Demo.ScoreTeam1 > Demo.ScoreTeam2 ? Demo.TeamCT : Demo.TeamT;
		}

		protected new void HandleRoundOfficiallyEnd(object sender, RoundOfficiallyEndedEventArgs e)
		{
			base.HandleRoundOfficiallyEnd(sender, e);

			if (!IsMatchStarted) return;

			if (Parser.CTScore == 15 && Parser.TScore == 15)
			{
				IsOvertime = true;
				CurrentOvertime = new Overtime()
				{
					Number = ++OvertimeCount
				};
			}
		}

		protected new void HandlePlayerKilled(object sender, PlayerKilledEventArgs e)
		{
			if (!IsMatchStarted ||e.Victim == null) return;

			Weapon weapon = Weapon.WeaponList.FirstOrDefault(w => w.Element == e.Weapon.Weapon);
			if (weapon == null) return;
			PlayerExtended killed = Demo.Players.FirstOrDefault(player => player.SteamId == e.Victim.SteamID);
			PlayerExtended killer = null;

			KillEvent killEvent = new KillEvent(Parser.IngameTick, Parser.CurrentTime)
			{
				KillerSteamId = e.Killer?.SteamID ?? 0,
				KillerName = e.Killer?.Name ?? "World",
				KillerSide = e.Killer?.Team ?? Team.Spectate,
				Weapon = weapon,
				KillerVelocityX = e.Killer?.Velocity.X ?? 0,
				KillerVelocityY = e.Killer?.Velocity.Y ?? 0,
				KillerVelocityZ = e.Killer?.Velocity.Z ?? 0,
				KilledSteamId = e.Victim.SteamID,
				KilledName = e.Victim.Name,
				KilledSide = e.Victim.Team,
				RoundNumber = CurrentRound.Number,
				IsKillerCrouching = e.Killer?.IsDucking ?? false,
				Point = new KillHeatmapPoint
				{
					KillerX = e.Killer?.Position.X ?? 0,
					KillerY = e.Killer?.Position.Y ?? 0,
					VictimX = e.Victim.Position.X,
					VictimY = e.Victim.Position.Y
				},
				KillerEntityId = e.Killer?.EntityID ?? -1,
				KilledEntityId = e.Victim.EntityID
			};

			bool killerIsBot = false;
			bool victimIsBot = false;
			bool assisterIsBot = false;

			if (e.Killer != null && e.Killer.SteamID == 0) killerIsBot = true;
			if (e.Victim.SteamID == 0) victimIsBot = true;
			if (e.Assister != null && e.Assister.SteamID == 0) assisterIsBot = true;
			if (e.Killer != null) killer = Demo.Players.FirstOrDefault(player => player.SteamId == e.Killer.SteamID);
			if (killer != null)
			{
				if (e.Killer.IsDucking) killer.CrouchKillCount++;
				if (e.Killer.Velocity.Z > 0) killer.JumpKillCount++;
				ProcessTradeKill(killEvent);
			}

			// Human killed human
			if (!killerIsBot && !victimIsBot)
			{
				if (killer != null && killed != null)
				{
					killed.IsAlive = false;
					// TK
					if (killer.TeamName == killed.TeamName)
					{
						killer.TeamKillCount++;
						killer.KillsCount--;
						if (killer.HeadshotCount > 0) killer.HeadshotCount--;
						killed.DeathCount++;
					}
					else
					{
						// Regular kill
						if (!killer.IsControllingBot)
						{
							killer.KillsCount++;
							if(e.Headshot) killer.HeadshotCount++;
						}
						if (!killed.IsControllingBot) killed.DeathCount++;
					}
				}
			}

			// Human killed a bot
			if (!killerIsBot && victimIsBot)
			{
				if (killer != null)
				{
					// TK
					if (killer.Side == e.Victim.Team)
					{
						killer.TeamKillCount++;
						killer.KillsCount--;
					}
					else
					{
						// Regular kill
						if (!killer.IsControllingBot)
						{
							killer.KillsCount++;
							if (e.Headshot) killer.HeadshotCount++;
						}
					}
				}
			}

			// A bot killed a human
			if (killerIsBot && !victimIsBot && killed != null)
			{
				// TK or not we add a death to the human
				killed.DeathCount++;
			}
		
			// Add assist if there was one
			if (e.Assister != null && !assisterIsBot && e.Assister.Team != e.Victim.Team)
			{
				PlayerExtended assister = Demo.Players.FirstOrDefault(player => player.SteamId == e.Assister.SteamID);
				if (assister != null)
				{
					killEvent.AssisterSteamId = e.Assister.SteamID;
					killEvent.AssisterName = e.Assister.Name;
					assister.AssistCount++;
				}
			}

			// If the killer isn't a bot we can update individual kill, open and entry kills
			if (e.Killer != null && killer != null && !killer.IsControllingBot)
			{
				if (!KillsThisRound.ContainsKey(e.Killer))
				{
					KillsThisRound[e.Killer] = 0;
				}
				KillsThisRound[e.Killer]++;

				ProcessOpenAndEntryKills(killEvent);
			}

			ProcessClutches();

			Demo.Kills.Add(killEvent);
			CurrentRound.Kills.Add(killEvent);
			if (AnalyzePlayersPosition)
			{
				PositionPoint positionPoint = new PositionPoint
				{
					X = e.Victim.Position.X,
					Y = e.Victim.Position.Y,
					PlayerSteamId = e.Killer?.SteamID ?? 0,
					PlayerName = e.Killer?.Name ?? string.Empty,
					Team = e.Killer?.Team ?? Team.Spectate,
					Event = killEvent,
					RoundNumber = CurrentRound.Number
				};
				Demo.PositionsPoint.Add(positionPoint);
			}
		}

		#endregion

		#region Process

		private void StartMatch()
		{
			RoundCount = 0;
			IsMatchStarted = true;

			if (!string.IsNullOrWhiteSpace(Parser.CTClanName)) Demo.TeamCT.Name = Parser.CTClanName;
			if (!string.IsNullOrWhiteSpace(Parser.TClanName)) Demo.TeamT.Name = Parser.TClanName;

			// Add all players to our ObservableCollection of PlayerExtended
			foreach (Player player in Parser.PlayingParticipants)
			{
				// don't add bot
				if (player.SteamID != 0)
				{
					PlayerExtended pl = new PlayerExtended
					{
						SteamId = player.SteamID,
						Name = player.Name,
						Side = player.Team
					};
					if (!Demo.Players.Contains(pl))
					{
						Application.Current.Dispatcher.Invoke(delegate
						{
							Demo.Players.Add(pl);
							if (pl.Side == Team.CounterTerrorist && !Demo.TeamCT.Players.Contains(pl))
							{
								Demo.TeamCT.Players.Add(pl);
								if (!Demo.TeamCT.Players.Contains(pl))
								{
									Demo.TeamCT.Players.Add(pl);
								}
								pl.TeamName = Demo.TeamCT.Name;
							}

							if (pl.Side == Team.Terrorist && !Demo.TeamT.Players.Contains(pl))
							{
								Demo.TeamT.Players.Add(pl);
								if (!Demo.TeamT.Players.Contains(pl))
								{
									Demo.TeamT.Players.Add(pl);
								}
								pl.TeamName = Demo.TeamT.Name;
							}
						});
					}
				}
			}

			// First round handled here because round_start is raised before begin_new_match
			CreateNewRound();
		}

		/// <summary>
		/// Set the correct clan name winner
		/// </summary>
		/// <param name="roundEndedEventArgs"></param>
		protected new void UpdateTeamScore(RoundEndedEventArgs roundEndedEventArgs)
		{
			if (IsOvertime)
			{
				if (IsHalfMatch)
				{
					if (roundEndedEventArgs.Winner == Team.CounterTerrorist)
					{
						CurrentRound.WinnerName = Demo.TeamT.Name;
						CurrentOvertime.ScoreTeam2++;
						Demo.ScoreTeam2++;
					}
					else
					{
						CurrentRound.WinnerName = Demo.TeamCT.Name;
						CurrentOvertime.ScoreTeam1++;
						Demo.ScoreTeam1++;
					}
				}
				else
				{
					if (roundEndedEventArgs.Winner == Team.CounterTerrorist)
					{
						CurrentRound.WinnerName = Demo.TeamCT.Name;
						CurrentOvertime.ScoreTeam1++;
						Demo.ScoreTeam1++;
					}
					else
					{
						CurrentRound.WinnerName = Demo.TeamT.Name;
						CurrentOvertime.ScoreTeam2++;
						Demo.ScoreTeam2++;
					}
				}
			}
			else
			{
				if (IsHalfMatch)
				{
					if (roundEndedEventArgs.Winner == Team.CounterTerrorist)
					{
						CurrentRound.WinnerName = Demo.TeamT.Name;
						Demo.ScoreSecondHalfTeam2++;
						Demo.ScoreTeam2++;
					}
					else
					{
						CurrentRound.WinnerName = Demo.TeamCT.Name;
						Demo.ScoreSecondHalfTeam1++;
						Demo.ScoreTeam1++;
					}
				}
				else
				{
					if (roundEndedEventArgs.Winner == Team.Terrorist)
					{
						CurrentRound.WinnerName = Demo.TeamT.Name;
						Demo.ScoreFirstHalfTeam2++;
						Demo.ScoreTeam2++;
					}
					else
					{
						CurrentRound.WinnerName = Demo.TeamCT.Name;
						Demo.ScoreFirstHalfTeam1++;
						Demo.ScoreTeam1++;
					}
				}
			}
		}

		#endregion
	}
}