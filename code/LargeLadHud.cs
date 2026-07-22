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
		DrawLargeLadStatus( hud, round );
		DrawRoleStatus( hud, player );
		DrawWeaponStatus( hud, player );
		DrawCrosshair( hud, player );

		if ( round.Phase == LargeLadRoundPhase.RoundOver )
		{
			DrawWinnerBanner( hud, round );
		}
	}

	private static void DrawLargeLadStatus(
		HudPainter hud,
		LargeLadRoundManager round )
	{
		if ( round.Phase != LargeLadRoundPhase.HeadStart &&
			round.Phase != LargeLadRoundPhase.Playing )
		{
			return;
		}

		var largeLad = round.Scene
			.GetAllComponents<LargeLadPlayer>()
			.FirstOrDefault( player => player.Role == LargeLadRole.LargeLad );
		var health = largeLad?.Health;

		if ( health is null )
			return;

		var accent = new Color( 1.0f, 0.32f, 0.10f );
		var centerX = Screen.Width * 0.5f;
		var panel = new Rect( centerX - 190.0f, 116.0f, 380.0f, 42.0f );

		DrawPanel( hud, panel, accent );

		var status = health.IsDead
			? $"LARGE LAD RESPAWNING IN {FormatSeconds( health.RespawnTimeRemaining )}"
			: $"LARGE LAD  {health.CurrentHealth:0} / {health.MaximumHealth:0}";

		hud.DrawText(
			status,
			16.0f,
			health.IsDead ? Color.White : accent,
			new Vector2( centerX, panel.Center.y ),
			TextFlag.Center );
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
		var panel = new Rect( 28.0f, Screen.Height - 130.0f, 360.0f, 102.0f );

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

		var health = player.Health;

		if ( health is null || player.Role == LargeLadRole.Unassigned )
			return;

		var healthText = health.IsDead
			? $"RESPAWNING IN {FormatSeconds( health.RespawnTimeRemaining )}"
			: $"HEALTH {health.CurrentHealth:0} / {health.MaximumHealth:0}";

		hud.DrawText(
			healthText,
			14.0f,
			health.IsDead ? new Color( 1.0f, 0.32f, 0.10f ) : accent,
			new Vector2( panel.Left + 18.0f, panel.Top + 80.0f ),
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

	private static void DrawCrosshair( HudPainter hud, LargeLadPlayer player )
	{
		if ( player.Health is null ||
			player.Health.IsDead ||
			player.Inventory is null ||
			player.EquippedWeapon == LargeLadWeaponId.None )
		{
			return;
		}

		var centerX = Screen.Width * 0.5f;
		var centerY = Screen.Height * 0.5f;

		var crosshair = player.Inventory.EquippedDefinition.Crosshair;

		if ( crosshair == LargeLadCrosshairStyle.Dot )
		{
			DrawCrosshairSegment(
				hud,
				new Rect( centerX - 2.5f, centerY - 2.5f, 5.0f, 5.0f ) );
			return;
		}

		if ( crosshair != LargeLadCrosshairStyle.FourSegment )
			return;

		const float gap = 6.0f;
		const float armLength = 10.0f;
		const float thickness = 2.0f;
		var halfThickness = thickness * 0.5f;

		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX - gap - armLength,
				centerY - halfThickness,
				armLength,
				thickness ) );
		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX + gap,
				centerY - halfThickness,
				armLength,
				thickness ) );
		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX - halfThickness,
				centerY - gap - armLength,
				thickness,
				armLength ) );
		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX - halfThickness,
				centerY + gap,
				thickness,
				armLength ) );
	}

	private static void DrawWeaponStatus( HudPainter hud, LargeLadPlayer player )
	{
		var inventory = player.Inventory;

		if ( player.Health?.IsDead != false || inventory is null ||
			inventory.EquippedWeapon == LargeLadWeaponId.None )
		{
			return;
		}

		var definition = inventory.EquippedDefinition;
		var accent = definition.PickupColor;
		var panel = new Rect(
			Screen.Width - 318.0f,
			Screen.Height - 116.0f,
			290.0f,
			88.0f );

		DrawPanel( hud, panel, accent );
		hud.DrawText(
			definition.DisplayName.ToUpperInvariant(),
			20.0f,
			accent,
			new Vector2( panel.Right - 18.0f, panel.Top + 24.0f ),
			TextFlag.RightCenter );

		if ( !definition.UsesAmmo )
			return;

		var ammoText = inventory.IsReloading
			? $"RELOADING {FormatSeconds( inventory.ReloadTimeRemaining )}"
			: $"{inventory.EquippedMagazine} / {inventory.EquippedReserve}";

		hud.DrawText(
			ammoText,
			18.0f,
			Color.White,
			new Vector2( panel.Right - 18.0f, panel.Top + 58.0f ),
			TextFlag.RightCenter );
	}

	private static void DrawCrosshairSegment( HudPainter hud, Rect rect )
	{
		DrawSolidRect(
			hud,
			new Rect(
				rect.Left - 1.0f,
				rect.Top - 1.0f,
				rect.Width + 2.0f,
				rect.Height + 2.0f ),
			new Color( 0.0f, 0.0f, 0.0f, 0.8f ) );

		DrawSolidRect( hud, rect, Color.White );
	}

	private static void DrawSolidRect( HudPainter hud, Rect rect, Color color )
	{
		hud.DrawRect(
			rect,
			color,
			new Vector4( 0.0f, 0.0f, 0.0f, 0.0f ),
			new Vector4( 0.0f, 0.0f, 0.0f, 0.0f ),
			new Color( 0.0f, 0.0f, 0.0f, 0.0f ) );
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
			LargeLadRoundPhase.Playing when player.Health?.IsDead == true =>
				$"Respawning in {FormatSeconds( player.Health.RespawnTimeRemaining )}",
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
			LargeLadRole.SkinnyKid => "Survive the timer. Find weapons placed in the map.",
			LargeLadRole.LargeLad => "Eat every Skinny Kid. Primary fire: melee.",
			LargeLadRole.Minion => "Help the Lad eat the Skinny Kids. Primary fire: melee.",
			_ => "A new round will begin shortly."
		};
	}

	private static string FormatSeconds( float seconds )
	{
		return $"{System.MathF.Max( 0.0f, System.MathF.Ceiling( seconds ) ):0}s";
	}
}
