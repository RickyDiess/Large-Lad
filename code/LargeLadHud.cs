using Sandbox;
using Sandbox.Rendering;

public sealed class LargeLadHud : Component
{
	private static readonly Color PanelColor = new( 0.025f, 0.03f, 0.045f, 0.88f );
	private static readonly Color MutedTextColor = new( 0.75f, 0.78f, 0.84f, 1.0f );
	private LargeLadPlayer cachedPlayer;
	private PlayerController cachedController;
	private LargeLadGameManager cachedGameManager;

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override void OnStart()
	{
		ResolveCachedReferences();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || Scene.Camera is null )
			return;

		var player = cachedPlayer;
		var round = cachedGameManager;

		if ( player is null ||
			round is null ||
			!round.IsValid ||
			!round.Enabled ||
			round.Scene != Scene ||
			!round.HasSceneGameplayOwnership )
			return;

		var hud = Scene.Camera.Hud;

		DrawRoundStatus( hud, round, player );
		DrawLargeLadStatus( hud, round );
		DrawRoleStatus( hud, player );
		DrawWeaponStatus( hud, player );
		DrawCrosshair( hud, player, cachedController );
		DrawConfirmedHitmarker( hud, player );

		if ( round.Phase == LargeLadRoundPhase.RoundOver )
		{
			DrawWinnerBanner( hud, round );
		}
	}

	private static void DrawLargeLadStatus(
		HudPainter hud,
		LargeLadGameManager round )
	{
		if ( round.Phase != LargeLadRoundPhase.HeadStart &&
			round.Phase != LargeLadRoundPhase.Playing )
		{
			return;
		}

		var largeLad = round.CurrentLargeLad;
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
		LargeLadGameManager round,
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

	private static void DrawWinnerBanner( HudPainter hud, LargeLadGameManager round )
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
			LargeLadWinner.LargeLadTeam => "THE LAD ATE EVERYONE",
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

	private static void DrawCrosshair(
		HudPainter hud,
		LargeLadPlayer player,
		PlayerController controller )
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

		var center = new Vector2( centerX, centerY );
		var definition = player.Inventory.EquippedDefinition;
		var validAim = LargeLadAimResolver.TryResolveLocal(
			player.Scene,
			player.Scene.Camera,
			controller,
			player.GameObject,
			definition.Range,
			out var aim );
		var intentColor = !validAim
			? new Color( 1.0f, 0.2f, 0.14f )
			: aim.IsObstructed
				? new Color( 1.0f, 0.72f, 0.12f )
				: Color.White;

		// This fixed marker is camera intent. In clear space it also represents
		// the predicted eye-origin impact, so the two markers collapse into one.
		DrawCrosshairSegment(
			hud,
			new Rect( centerX - 1.5f, centerY - 1.5f, 3.0f, 3.0f ),
			intentColor );

		if ( !validAim )
		{
			DrawInvalidAimMarker( hud, center );
			return;
		}

		if ( !aim.IsObstructed )
			return;

		if ( !TryProjectImpactPoint(
			player.Scene.Camera,
			aim.ActualImpactPoint,
			out var impactPosition ) )
		{
			DrawInvalidAimMarker( hud, center );
			return;
		}

		DrawImpactMarker(
			hud,
			impactPosition,
			new Color( 1.0f, 0.72f, 0.12f ) );
	}

	private static void DrawConfirmedHitmarker(
		HudPainter hud,
		LargeLadPlayer player )
	{
		if ( player.EquippedWeapon == LargeLadWeaponId.Melee )
		{
			var melee = player.MeleeCombat;

			if ( melee?.HasConfirmedHitmarker != true )
				return;

			var meleeCenter = new Vector2(
				Screen.Width * 0.5f,
				Screen.Height * 0.5f );
			var meleeColor =
				melee.LastAttackResult == LargeLadMeleeResult.BarricadeHit
					? new Color( 1.0f, 0.72f, 0.12f )
					: Color.White;

			DrawDiagonalMarker(
				hud,
				meleeCenter,
				8.0f,
				14.0f,
				meleeColor );
			return;
		}

		var weapon = player.PrototypeWeapon;

		if ( weapon?.HasConfirmedHitmarker != true )
			return;

		var center = new Vector2( Screen.Width * 0.5f, Screen.Height * 0.5f );
		var color = weapon.LastShotResult == LargeLadShotResult.BarricadeHit
			? new Color( 1.0f, 0.72f, 0.12f )
			: Color.White;

		DrawDiagonalMarker( hud, center, 8.0f, 14.0f, color );
	}

	private static bool TryProjectImpactPoint(
		CameraComponent camera,
		Vector3 impactPoint,
		out Vector2 screenPosition )
	{
		screenPosition = default;

		if ( camera is null || !LargeLadAimResolver.IsFinite( impactPoint ) )
			return false;

		var view = camera.View;
		var towardImpact = impactPoint - view.Position;
		var depth = Vector3.Dot( towardImpact, view.Rotation.Forward );

		if ( !LargeLadAimResolver.IsFinite( view.Position ) ||
			!LargeLadAimResolver.IsFinite( towardImpact ) ||
			!float.IsFinite( depth ) || depth <= 0.001f ||
			!float.IsFinite( view.FieldOfView ) ||
			view.FieldOfView <= 0.0f || view.FieldOfView >= 179.0f ||
			Screen.Width <= 0.0f || Screen.Height <= 0.0f )
		{
			return false;
		}

		var aspect = Screen.Width / Screen.Height;
		var fieldOfViewTangent = System.MathF.Tan(
			view.FieldOfView * System.MathF.PI / 360.0f );
		var horizontalTangent = camera.FovAxis == CameraComponent.Axis.Horizontal
			? fieldOfViewTangent
			: fieldOfViewTangent * aspect;
		var verticalTangent = camera.FovAxis == CameraComponent.Axis.Vertical
			? fieldOfViewTangent
			: fieldOfViewTangent / aspect;
		var normalizedX = 0.5f +
			Vector3.Dot( towardImpact, view.Rotation.Right ) /
			(depth * horizontalTangent) * 0.5f;
		var normalizedY = 0.5f -
			Vector3.Dot( towardImpact, view.Rotation.Up ) /
			(depth * verticalTangent) * 0.5f;

		if ( !float.IsFinite( normalizedX ) || !float.IsFinite( normalizedY ) ||
			normalizedX < 0.0f || normalizedX > 1.0f ||
			normalizedY < 0.0f || normalizedY > 1.0f )
		{
			return false;
		}

		screenPosition = new Vector2(
			normalizedX * Screen.Width,
			normalizedY * Screen.Height );
		return true;
	}

	private static void DrawImpactMarker(
		HudPainter hud,
		Vector2 center,
		Color color )
	{
		const float gap = 4.0f;
		const float length = 5.0f;
		const float thickness = 2.0f;

		DrawCrosshairSegment(
			hud,
			new Rect( center.x - gap - length, center.y - 1.0f, length, thickness ),
			color );
		DrawCrosshairSegment(
			hud,
			new Rect( center.x + gap, center.y - 1.0f, length, thickness ),
			color );
		DrawCrosshairSegment(
			hud,
			new Rect( center.x - 1.0f, center.y - gap - length, thickness, length ),
			color );
		DrawCrosshairSegment(
			hud,
			new Rect( center.x - 1.0f, center.y + gap, thickness, length ),
			color );
	}

	private static void DrawInvalidAimMarker(
		HudPainter hud,
		Vector2 center )
	{
		DrawDiagonalMarker(
			hud,
			center,
			4.0f,
			9.0f,
			new Color( 1.0f, 0.2f, 0.14f ) );
	}

	private static void DrawDiagonalMarker(
		HudPainter hud,
		Vector2 center,
		float innerRadius,
		float outerRadius,
		Color color )
	{
		var directions = new[]
		{
			new Vector2( -1.0f, -1.0f ),
			new Vector2( 1.0f, -1.0f ),
			new Vector2( -1.0f, 1.0f ),
			new Vector2( 1.0f, 1.0f )
		};

		foreach ( var direction in directions )
		{
			var normalized = direction.Normal;
			var start = center + normalized * innerRadius;
			var end = center + normalized * outerRadius;
			hud.DrawLine( start, end, 4.0f, new Color( 0.0f, 0.0f, 0.0f, 0.8f ), default );
			hud.DrawLine( start, end, 2.0f, color, default );
		}
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
		DrawCrosshairSegment( hud, rect, Color.White );
	}

	private static void DrawCrosshairSegment(
		HudPainter hud,
		Rect rect,
		Color color )
	{
		DrawSolidRect(
			hud,
			new Rect(
				rect.Left - 1.0f,
				rect.Top - 1.0f,
				rect.Width + 2.0f,
				rect.Height + 2.0f ),
			new Color( 0.0f, 0.0f, 0.0f, 0.8f ) );

		DrawSolidRect( hud, rect, color );
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

	private static string GetPhaseTitle( LargeLadGameManager round )
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
		LargeLadGameManager round,
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

	private static Color GetPhaseColor( LargeLadGameManager round )
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
			LargeLadRole.SkinnyKid => "Melee through early barricades. Find mapped weapons and survive.",
			LargeLadRole.LargeLad => "Eat every Skinny Kid. Primary fire: melee.",
			LargeLadRole.Minion => "Help the Lad eat the Skinny Kids. Primary fire: melee.",
			_ => "A new round will begin shortly."
		};
	}

	private static string FormatSeconds( float seconds )
	{
		return $"{System.MathF.Max( 0.0f, System.MathF.Ceiling( seconds ) ):0}s";
	}

	private LargeLadGameManager GetGameManager()
	{
		if ( cachedGameManager is not null &&
			cachedGameManager.IsValid &&
			cachedGameManager.Enabled &&
			cachedGameManager.Scene == Scene &&
			cachedGameManager.HasSceneGameplayOwnership )
		{
			return cachedGameManager;
		}

		cachedGameManager = LargeLadGameManager.FindForScene( Scene );
		return cachedGameManager;
	}

	private void ResolveCachedReferences()
	{
		if ( cachedPlayer is null ||
			!cachedPlayer.IsValid ||
			cachedPlayer.GameObject != GameObject )
		{
			cachedPlayer = Components.Get<LargeLadPlayer>();
		}

		if ( cachedController is null ||
			!cachedController.IsValid ||
			cachedController.GameObject != GameObject )
		{
			cachedController = Components.Get<PlayerController>();
		}

		GetGameManager();
	}
}
