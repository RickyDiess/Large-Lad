using Sandbox;
using Sandbox.Rendering;
using System.Linq;

public sealed class LargeLadHud : Component
{
	private static readonly Color PanelColor = new( 0.025f, 0.03f, 0.045f, 0.88f );
	private static readonly Color MutedTextColor = new( 0.75f, 0.78f, 0.84f, 1.0f );

	protected override void OnUpdate()
	{
		if ( IsProxy || Scene.Camera is null )
			return;

		var player = Components.Get<LargeLadPlayer>();
		var round = Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();

		if ( player is null || round is null )
			return;

		var hud = Scene.Camera.Hud;

		DrawRoundStatus( hud, round, player );
		DrawRoleStatus( hud, player );

		if ( round.Phase == LargeLadRoundPhase.RoundOver )
		{
			DrawWinnerBanner( hud, round );
		}
	}

	private static void DrawRoundStatus(
		HudPainter hud,
		LargeLadRoundManager round,
		LargeLadPlayer player )
	{
		var centerX = Screen.Width * 0.5f;
		var accent = GetPhaseColor( round );
		var panel = new Rect( centerX - 190.0f, 28.0f, 380.0f, 78.0f );

		DrawPanel( hud, panel, accent );

		hud.DrawText(
			GetPhaseTitle( round ),
			22.0f,
			Color.White,
			new Vector2( centerX, 51.0f ),
			TextFlag.Center );

		hud.DrawText(
			GetPhaseSubtitle( round, player ),
			16.0f,
			accent,
			new Vector2( centerX, 82.0f ),
			TextFlag.Center );
	}

	private static void DrawRoleStatus( HudPainter hud, LargeLadPlayer player )
	{
		var accent = GetRoleColor( player.Role );
		var panel = new Rect( 28.0f, Screen.Height - 104.0f, 330.0f, 76.0f );

		DrawPanel( hud, panel, accent );

		hud.DrawText(
			$"ROLE: {GetRoleName( player.Role )}",
			20.0f,
			accent,
			new Vector2( panel.Left + 18.0f, panel.Top + 24.0f ),
			TextFlag.LeftCenter );

		hud.DrawText(
			GetRoleObjective( player.Role ),
			14.0f,
			MutedTextColor,
			new Vector2( panel.Left + 18.0f, panel.Top + 53.0f ),
			TextFlag.LeftCenter );
	}

	private static void DrawWinnerBanner( HudPainter hud, LargeLadRoundManager round )
	{
		var center = new Vector2( Screen.Width * 0.5f, Screen.Height * 0.42f );
		var accent = round.Winner == LargeLadWinner.SkinnyKids
			? new Color( 0.25f, 0.85f, 1.0f )
			: new Color( 1.0f, 0.32f, 0.10f );

		var panel = new Rect( center.x - 250.0f, center.y - 52.0f, 500.0f, 104.0f );
		DrawPanel( hud, panel, accent );

		var winnerText = round.Winner switch
		{
			LargeLadWinner.SkinnyKids => "SKINNY KIDS SURVIVED",
			LargeLadWinner.LargeLadTeam => "LAD TEAM WINS",
			_ => "ROUND OVER"
		};

		hud.DrawText(
			winnerText,
			34.0f,
			Color.White,
			new Vector2( center.x, center.y - 10.0f ),
			TextFlag.Center );

		hud.DrawText(
			$"Next round in {FormatSeconds( round.PhaseTimeRemaining )}",
			16.0f,
			accent,
			new Vector2( center.x, center.y + 27.0f ),
			TextFlag.Center );
	}

	private static void DrawPanel( HudPainter hud, Rect rect, Color accent )
	{
		hud.DrawRect(
			rect,
			PanelColor,
			new Vector4( 10.0f, 10.0f, 10.0f, 10.0f ),
			new Vector4( 1.5f, 1.5f, 1.5f, 1.5f ),
			accent.WithAlpha( 0.8f ) );
	}

	private static string GetPhaseTitle( LargeLadRoundManager round )
	{
		return round.Phase switch
		{
			LargeLadRoundPhase.WaitingForPlayers => "WAITING FOR PLAYERS",
			LargeLadRoundPhase.HeadStart => "HEAD START",
			LargeLadRoundPhase.Playing => "ROUND IN PROGRESS",
			LargeLadRoundPhase.RoundOver => "INTERMISSION",
			_ => "LARGE LAD"
		};
	}

	private static string GetPhaseSubtitle(
		LargeLadRoundManager round,
		LargeLadPlayer player )
	{
		return round.Phase switch
		{
			LargeLadRoundPhase.WaitingForPlayers => "Waiting for another player...",
			LargeLadRoundPhase.HeadStart when player.Role == LargeLadRole.LargeLad =>
				$"Released in {FormatSeconds( round.PhaseTimeRemaining )}",
			LargeLadRoundPhase.HeadStart =>
				$"Run! {FormatSeconds( round.PhaseTimeRemaining )}",
			LargeLadRoundPhase.Playing =>
				$"{FormatSeconds( round.PhaseTimeRemaining )} remaining",
			LargeLadRoundPhase.RoundOver =>
				$"Next round in {FormatSeconds( round.PhaseTimeRemaining )}",
			_ => string.Empty
		};
	}

	private static Color GetPhaseColor( LargeLadRoundManager round )
	{
		return round.Phase switch
		{
			LargeLadRoundPhase.WaitingForPlayers => new Color( 0.65f, 0.68f, 0.75f ),
			LargeLadRoundPhase.HeadStart => new Color( 1.0f, 0.78f, 0.18f ),
			LargeLadRoundPhase.Playing => new Color( 0.25f, 0.85f, 1.0f ),
			LargeLadRoundPhase.RoundOver when round.Winner == LargeLadWinner.SkinnyKids =>
				new Color( 0.25f, 0.85f, 1.0f ),
			LargeLadRoundPhase.RoundOver => new Color( 1.0f, 0.32f, 0.10f ),
			_ => Color.White
		};
	}

	private static Color GetRoleColor( LargeLadRole role )
	{
		return role switch
		{
			LargeLadRole.SkinnyKid => new Color( 0.25f, 0.85f, 1.0f ),
			LargeLadRole.LargeLad => new Color( 1.0f, 0.32f, 0.10f ),
			LargeLadRole.Minion => new Color( 0.72f, 0.35f, 1.0f ),
			_ => new Color( 0.65f, 0.68f, 0.75f )
		};
	}

	private static string GetRoleName( LargeLadRole role )
	{
		return role switch
		{
			LargeLadRole.SkinnyKid => "SKINNY KID",
			LargeLadRole.LargeLad => "LARGE LAD",
			LargeLadRole.Minion => "MINION",
			_ => "UNASSIGNED"
		};
	}

	private static string GetRoleObjective( LargeLadRole role )
	{
		return role switch
		{
			LargeLadRole.SkinnyKid => "Survive until the timer expires.",
			LargeLadRole.LargeLad => "Convert every Skinny Kid.",
			LargeLadRole.Minion => "Help the Lad convert the remaining Skinny Kids.",
			_ => "A new round will begin shortly."
		};
	}

	private static string FormatSeconds( float seconds )
	{
		return $"{System.MathF.Max( 0.0f, System.MathF.Ceiling( seconds ) ):0}s";
	}
}
