﻿using DemoInfo;
using Newtonsoft.Json;

namespace CSGO_Demos_Manager.Models.Events
{
	public class NadeBaseEvent : BaseEvent
	{
		[JsonProperty("thrower_steamid")]
		public long ThrowerSteamId { get; set; }

		[JsonProperty("thrower_name")]
		public string ThrowerName { get; set; }

		[JsonProperty("thrower_side")]
		public Team ThrowerSide { get; set; }

		[JsonProperty("heatmap_point")]
		public HeatmapPoint Point { get; set; }

		[JsonProperty("round_number")]
		public int RoundNumber { get; set; }

		public NadeBaseEvent(int tick, float seconds) : base(tick, seconds) { }
	}
}
