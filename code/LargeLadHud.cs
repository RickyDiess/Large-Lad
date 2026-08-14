using Sandbox;
using Sandbox.Rendering;

public sealed class LargeLadHud : Component
{
	private const string HudFont = "Roboto Condensed";
	private const int RegularFontWeight = 400;
	private const int BoldFontWeight = 700;
	private const float ReferenceWidth = 1920.0f;
	private const float ReferenceHeight = 1080.0f;
	private const float MinimumHudScale = 0.85f;
	private const float MaximumHudScale = 2.0f;
	private static readonly Color PanelColor =
		new( 0.025f, 0.03f, 0.045f, 0.78f );
	private static readonly Color BarTrackColor =
		new( 0.72f, 0.76f, 0.84f, 0.18f );
	private static readonly Color MutedTextColor =
		new( 0.75f, 0.78f, 0.84f, 1.0f );
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
		var scale = GetHudScale();

		DrawRoundStatus( hud, round, scale );
		DrawLargeLadStatus( hud, round, scale );
		DrawBarricadeDestructionAnnouncement( hud, round, scale );
		DrawLastSkinnyKidAnnouncement( hud, round, scale );
		DrawRoleStatus( hud, player, scale );
		DrawWeaponStatus( hud, player, scale );
		DrawGroundSlamFeedback( hud, player, scale );
		DrawPickupFeedback( hud, player, scale );
		DrawCrosshair( hud, player, cachedController, scale );
		DrawConfirmedHitmarker( hud, player, scale );

		if ( round.Phase == LargeLadRoundPhase.RoundOver )
		{
			DrawWinnerBanner( hud, round, scale );
		}
	}

	private static void DrawLargeLadStatus(
		HudPainter hud,
		LargeLadGameManager round,
		float scale )
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

		var accent = GetRoleColor( LargeLadRole.LargeLad );
		var centerX = Screen.Width * 0.5f;
		var panel = new Rect(
			centerX - 180.0f * scale,
			72.0f * scale,
			360.0f * scale,
			32.0f * scale );

		DrawPanel( hud, panel, accent, scale );

		var labelRect = new Rect(
			panel.Left + 14.0f * scale,
			panel.Top,
			82.0f * scale,
			panel.Height );
		DrawHudText(
			hud,
			"LARGE LAD",
			12.0f * scale,
			10.0f * scale,
			accent,
			labelRect,
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );

		if ( health.IsDead )
		{
			DrawHudText(
				hud,
				$"RESPAWN {FormatSeconds( health.RespawnTimeRemaining )}",
				13.0f * scale,
				11.0f * scale,
				Color.White,
				new Rect(
					panel.Left + 102.0f * scale,
					panel.Top,
					panel.Width - 116.0f * scale,
					panel.Height ),
				TextFlag.RightCenter | TextFlag.SingleLine,
				BoldFontWeight );
			return;
		}

		var healthBar = new Rect(
			panel.Left + 104.0f * scale,
			panel.Center.y - 3.0f * scale,
			174.0f * scale,
			6.0f * scale );
		DrawBar(
			hud,
			healthBar,
			GetHealthFraction( health.CurrentHealth, health.MaximumHealth ),
			accent,
			scale );
		DrawHudText(
			hud,
			$"{health.CurrentHealth:0}",
			14.0f * scale,
			11.0f * scale,
			Color.White,
			new Rect(
				healthBar.Right + 8.0f * scale,
				panel.Top,
				panel.Right - healthBar.Right - 20.0f * scale,
				panel.Height ),
			TextFlag.RightCenter | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawRoundStatus(
		HudPainter hud,
		LargeLadGameManager round,
		float scale )
	{
		var centerX = Screen.Width * 0.5f;
		var accent = GetPhaseColor( round );
		var panel = new Rect(
			centerX - 180.0f * scale,
			24.0f * scale,
			360.0f * scale,
			40.0f * scale );

		DrawPanel( hud, panel, accent, scale );

		var hasTimer = round.Phase != LargeLadRoundPhase.WaitingForPlayers;
		var titleRect = new Rect(
			panel.Left + 16.0f * scale,
			panel.Top,
			panel.Width - (hasTimer ? 112.0f : 32.0f) * scale,
			panel.Height );
		DrawHudText(
			hud,
			GetPhaseTitle( round ),
			16.0f * scale,
			12.0f * scale,
			Color.White,
			titleRect,
			hasTimer
				? TextFlag.LeftCenter | TextFlag.SingleLine
				: TextFlag.Center | TextFlag.SingleLine,
			BoldFontWeight );

		if ( !hasTimer )
			return;

		DrawHudText(
			hud,
			FormatClock( round.PhaseTimeRemaining ),
			20.0f * scale,
			16.0f * scale,
			accent,
			new Rect(
				panel.Right - 94.0f * scale,
				panel.Top,
				78.0f * scale,
				panel.Height ),
			TextFlag.RightCenter | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawBarricadeDestructionAnnouncement(
		HudPainter hud,
		LargeLadGameManager round,
		float scale )
	{
		if ( !round.HasBarricadeDestructionAnnouncement ||
			string.IsNullOrWhiteSpace(
				round.BarricadeDestructionAnnouncement ) )
		{
			return;
		}

		var centerX = Screen.Width * 0.5f;
		var panel = new Rect(
			centerX - 210.0f * scale,
			112.0f * scale,
			420.0f * scale,
			44.0f * scale );
		var accent = new Color( 0.25f, 0.85f, 1.0f );
		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			round.BarricadeDestructionAnnouncement,
			15.0f * scale,
			12.0f * scale,
			Color.White,
			Inset( panel, 16.0f * scale, 6.0f * scale ),
			TextFlag.Center | TextFlag.WordWrap,
			RegularFontWeight );
	}

	private static void DrawLastSkinnyKidAnnouncement(
		HudPainter hud,
		LargeLadGameManager round,
		float scale )
	{
		if ( !round.HasLastSkinnyKidAnnouncement ||
			string.IsNullOrWhiteSpace( round.LastSkinnyKidAnnouncement ) )
		{
			return;
		}

		var center = new Vector2(
			Screen.Width * 0.5f,
			Screen.Height * 0.31f );
		var panel = new Rect(
			center.x - 220.0f * scale,
			center.y - 32.0f * scale,
			440.0f * scale,
			64.0f * scale );
		var accent = new Color( 0.25f, 0.85f, 1.0f );
		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			round.LastSkinnyKidAnnouncement,
			28.0f * scale,
			22.0f * scale,
			Color.White,
			Inset( panel, 18.0f * scale, 8.0f * scale ),
			TextFlag.Center | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawRoleStatus(
		HudPainter hud,
		LargeLadPlayer player,
		float scale )
	{
		if ( player.Role == LargeLadRole.Unassigned )
			return;

		var accent = GetRoleColor( player.Role );
		var panel = new Rect(
			24.0f * scale,
			Screen.Height - 88.0f * scale,
			260.0f * scale,
			64.0f * scale );

		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			GetRoleName( player.Role ),
			15.0f * scale,
			12.0f * scale,
			accent,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Top + 7.0f * scale,
				150.0f * scale,
				22.0f * scale ),
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );

		var health = player.Health;

		if ( health is null )
			return;

		if ( health.IsDead )
		{
			DrawHudText(
				hud,
				$"RESPAWN {FormatSeconds( health.RespawnTimeRemaining )}",
				17.0f * scale,
				13.0f * scale,
				GetRoleColor( LargeLadRole.LargeLad ),
				new Rect(
					panel.Left + 124.0f * scale,
					panel.Top + 7.0f * scale,
					panel.Width - 138.0f * scale,
					22.0f * scale ),
				TextFlag.RightCenter | TextFlag.SingleLine,
				BoldFontWeight );
			return;
		}

		DrawHudText(
			hud,
			$"{health.CurrentHealth:0}",
			20.0f * scale,
			15.0f * scale,
			Color.White,
			new Rect(
				panel.Right - 70.0f * scale,
				panel.Top + 5.0f * scale,
				56.0f * scale,
				26.0f * scale ),
			TextFlag.RightCenter | TextFlag.SingleLine,
			BoldFontWeight );
		DrawBar(
			hud,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Bottom - 15.0f * scale,
				panel.Width - 28.0f * scale,
				6.0f * scale ),
			GetHealthFraction( health.CurrentHealth, health.MaximumHealth ),
			accent,
			scale );
	}

	private static void DrawWinnerBanner(
		HudPainter hud,
		LargeLadGameManager round,
		float scale )
	{
		var center = new Vector2( Screen.Width * 0.5f, Screen.Height * 0.42f );
		var accent = round.Winner == LargeLadWinner.SkinnyKids
			? new Color( 0.25f, 0.85f, 1.0f )
			: new Color( 1.0f, 0.32f, 0.10f );

		var panel = new Rect(
			center.x - 240.0f * scale,
			center.y - 42.0f * scale,
			480.0f * scale,
			84.0f * scale );
		DrawPanel( hud, panel, accent, scale );

		var winnerText = round.Winner switch
		{
			LargeLadWinner.SkinnyKids => "SKINNY KIDS SURVIVED",
			LargeLadWinner.LargeLadTeam => "THE LAD ATE EVERYONE",
			_ => "ROUND OVER"
		};

		DrawHudText(
			hud,
			winnerText,
			28.0f * scale,
			21.0f * scale,
			Color.White,
			Inset( panel, 20.0f * scale, 12.0f * scale ),
			TextFlag.Center | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawCrosshair(
		HudPainter hud,
		LargeLadPlayer player,
		PlayerController controller,
		float scale )
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
		var markerScale = GetCrosshairScale( scale );

		var crosshair = LargeLadWeaponCatalog.Get(
			player.EquippedWeapon ).Crosshair;

		if ( crosshair == LargeLadCrosshairStyle.Dot )
		{
			var dotSize = 4.0f * markerScale;
			DrawCrosshairSegment(
				hud,
				new Rect(
					centerX - dotSize * 0.5f,
					centerY - dotSize * 0.5f,
					dotSize,
					dotSize ),
				Color.White,
				markerScale );
			return;
		}

		if ( crosshair != LargeLadCrosshairStyle.FourSegment )
			return;

		var gap = 5.0f * markerScale;
		var armLength = 8.0f * markerScale;
		var thickness = 1.5f * markerScale;
		var halfThickness = thickness * 0.5f;

		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX - gap - armLength,
				centerY - halfThickness,
				armLength,
				thickness ),
			Color.White,
			markerScale );
		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX + gap,
				centerY - halfThickness,
				armLength,
				thickness ),
			Color.White,
			markerScale );
		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX - halfThickness,
				centerY - gap - armLength,
				thickness,
				armLength ),
			Color.White,
			markerScale );
		DrawCrosshairSegment(
			hud,
			new Rect(
				centerX - halfThickness,
				centerY + gap,
				thickness,
				armLength ),
			Color.White,
			markerScale );

		var center = new Vector2( centerX, centerY );
		var definition = LargeLadWeaponCatalog.Get(
			player.EquippedWeapon );
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
			new Rect(
				centerX - 1.25f * markerScale,
				centerY - 1.25f * markerScale,
				2.5f * markerScale,
				2.5f * markerScale ),
			intentColor,
			markerScale );

		if ( !validAim )
		{
			DrawInvalidAimMarker( hud, center, markerScale );
			return;
		}

		if ( !aim.IsObstructed )
			return;

		if ( !TryProjectImpactPoint(
			player.Scene.Camera,
			aim.ActualImpactPoint,
			out var impactPosition ) )
		{
			DrawInvalidAimMarker( hud, center, markerScale );
			return;
		}

		DrawImpactMarker(
			hud,
			impactPosition,
			new Color( 1.0f, 0.72f, 0.12f ),
			markerScale );
	}

	private static void DrawConfirmedHitmarker(
		HudPainter hud,
		LargeLadPlayer player,
		float scale )
	{
		var markerScale = GetCrosshairScale( scale );

		if ( player.EquippedWeapon == LargeLadWeaponId.Melee )
		{
			var melee = player.MeleeCombat;

			if ( melee?.HasConfirmedHitmarker != true )
				return;

			var meleeCenter = new Vector2(
				Screen.Width * 0.5f,
				Screen.Height * 0.5f );
			var meleeColor =
				melee.LastAttackResult is
					LargeLadMeleeResult.BarricadeHit or
					LargeLadMeleeResult.PassageCoverHit
					? new Color( 1.0f, 0.72f, 0.12f )
					: Color.White;

			DrawDiagonalMarker(
				hud,
				meleeCenter,
				7.0f,
				13.0f,
				meleeColor,
				markerScale );
			return;
		}

		var weapon = player.PrototypeWeapon;

		if ( weapon?.HasConfirmedHitmarker != true )
			return;

		var center = new Vector2( Screen.Width * 0.5f, Screen.Height * 0.5f );
		if ( weapon.LastShotResult == LargeLadShotResult.PlayerHeadshot )
		{
			var headshotColor = new Color( 1.0f, 0.22f, 0.12f );
			DrawDiagonalMarker(
				hud,
				center,
				7.0f,
				16.0f,
				headshotColor,
				markerScale );
			DrawHudText(
				hud,
				"HEADSHOT",
				12.0f * markerScale,
				10.0f * markerScale,
				headshotColor,
				new Rect(
					center.x - 70.0f * markerScale,
					center.y + 23.0f * markerScale,
					140.0f * markerScale,
					20.0f * markerScale ),
				TextFlag.Center | TextFlag.SingleLine,
				BoldFontWeight );
			return;
		}

		var color = weapon.LastShotResult == LargeLadShotResult.BarricadeHit
			? new Color( 1.0f, 0.72f, 0.12f )
			: Color.White;

		DrawDiagonalMarker(
			hud,
			center,
			7.0f,
			13.0f,
			color,
			markerScale );
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
		Color color,
		float scale )
	{
		var gap = 3.5f * scale;
		var length = 4.0f * scale;
		var thickness = 1.5f * scale;
		var halfThickness = thickness * 0.5f;

		DrawCrosshairSegment(
			hud,
			new Rect(
				center.x - gap - length,
				center.y - halfThickness,
				length,
				thickness ),
			color,
			scale );
		DrawCrosshairSegment(
			hud,
			new Rect(
				center.x + gap,
				center.y - halfThickness,
				length,
				thickness ),
			color,
			scale );
		DrawCrosshairSegment(
			hud,
			new Rect(
				center.x - halfThickness,
				center.y - gap - length,
				thickness,
				length ),
			color,
			scale );
		DrawCrosshairSegment(
			hud,
			new Rect(
				center.x - halfThickness,
				center.y + gap,
				thickness,
				length ),
			color,
			scale );
	}

	private static void DrawInvalidAimMarker(
		HudPainter hud,
		Vector2 center,
		float scale )
	{
		DrawDiagonalMarker(
			hud,
			center,
			4.0f,
			9.0f,
			new Color( 1.0f, 0.2f, 0.14f ),
			scale );
	}

	private static void DrawDiagonalMarker(
		HudPainter hud,
		Vector2 center,
		float innerRadius,
		float outerRadius,
		Color color,
		float scale )
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
			var start = center + normalized * innerRadius * scale;
			var end = center + normalized * outerRadius * scale;
			hud.DrawLine(
				start,
				end,
				3.0f * scale,
				new Color( 0.0f, 0.0f, 0.0f, 0.8f ),
				default );
			hud.DrawLine( start, end, 1.5f * scale, color, default );
		}
	}

	private static void DrawWeaponStatus(
		HudPainter hud,
		LargeLadPlayer player,
		float scale )
	{
		var inventory = player.Inventory;

		if ( player.Health?.IsDead != false )
			return;

		if ( player.Role is LargeLadRole.LargeLad or LargeLadRole.Minion )
		{
			DrawBuiltInRoleAttackStatus( hud, player.Role, scale );
			return;
		}

		if ( player.Role != LargeLadRole.SkinnyKid || inventory is null )
			return;

		if ( player.NativeInventory?.ActiveFirearm is LargeLadFirearm nativeFirearm )
		{
			DrawNativeFirearmStatus(
				hud,
				player,
				nativeFirearm,
				scale );
			return;
		}

		if ( !TryGetSelectionPresentation(
			player,
			player.ActiveInventorySelection,
			out var displayName,
			out var accent,
			out var activeState,
			out var isFirearm,
			out var tag ) )
		{
			return;
		}

		var panel = new Rect(
			Screen.Width - 324.0f * scale,
			Screen.Height - 96.0f * scale,
			300.0f * scale,
			72.0f * scale );

		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			displayName.ToUpperInvariant(),
			18.0f * scale,
			13.0f * scale,
			accent,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Top + 6.0f * scale,
				isFirearm ? 152.0f * scale : panel.Width - 28.0f * scale,
				24.0f * scale ),
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );

		if ( isFirearm )
		{
			var ammo = activeState.HasInfiniteReserve
				? $"{activeState.Magazine} / \u221E"
				: $"{activeState.Magazine} / {activeState.Reserve}";
			DrawHudText(
				hud,
				ammo,
				22.0f * scale,
				16.0f * scale,
				Color.White,
				new Rect(
					panel.Right - 128.0f * scale,
					panel.Top + 4.0f * scale,
					114.0f * scale,
					28.0f * scale ),
				TextFlag.RightCenter | TextFlag.SingleLine,
				BoldFontWeight );
		}

		var status = tag;
		if ( !string.IsNullOrWhiteSpace( status ) )
		{
			DrawHudText(
				hud,
				status,
				10.0f * scale,
				8.0f * scale,
				MutedTextColor,
				new Rect(
					panel.Left + 14.0f * scale,
					panel.Top + 27.0f * scale,
					panel.Width - 28.0f * scale,
					13.0f * scale ),
				TextFlag.LeftCenter | TextFlag.SingleLine,
				BoldFontWeight );
		}

		DrawInventorySlotRail( hud, player, panel, scale );
	}

	private static void DrawNativeFirearmStatus(
		HudPainter hud,
		LargeLadPlayer player,
		LargeLadFirearm firearm,
		float scale )
	{
		var definition = LargeLadWeaponCatalog.Get( firearm.WeaponId );
		var accent = definition.PickupColor;
		var panel = new Rect(
			Screen.Width - 324.0f * scale,
			Screen.Height - 96.0f * scale,
			300.0f * scale,
			72.0f * scale );

		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			definition.DisplayName.ToUpperInvariant(),
			18.0f * scale,
			13.0f * scale,
			accent,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Top + 6.0f * scale,
				152.0f * scale,
				24.0f * scale ),
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );
		DrawInventorySlotRail( hud, player, panel, scale );
		DrawHudText(
			hud,
			firearm.IsExclusive
				? $"{firearm.Clip1} / {firearm.ExclusiveReserve}"
				: $"{firearm.Clip1} / \u221E",
			22.0f * scale,
			16.0f * scale,
			Color.White,
			new Rect(
				panel.Right - 128.0f * scale,
				panel.Top + 4.0f * scale,
				114.0f * scale,
				28.0f * scale ),
			TextFlag.RightCenter | TextFlag.SingleLine,
			BoldFontWeight );
		DrawHudText(
			hud,
			firearm.IsReloading
				? firearm.IsExclusive
					? "EXCLUSIVE / RELOADING"
					: "CORE / RELOADING"
				: firearm.IsExclusive ? "EXCLUSIVE" : "CORE",
			10.0f * scale,
			8.0f * scale,
			MutedTextColor,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Top + 27.0f * scale,
				panel.Width - 28.0f * scale,
				13.0f * scale ),
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawInventorySlotRail(
		HudPainter hud,
		LargeLadPlayer player,
		Rect panel,
		float scale )
	{
		var selectionCount = player.InventorySelectionCount;
		if ( selectionCount <= 0 )
			return;

		var gap = 4.0f * scale;
		var availableWidth = panel.Width - 28.0f * scale;
		var slotWidth = System.MathF.Min(
			24.0f * scale,
			(availableWidth - gap * (selectionCount - 1)) / selectionCount );
		var railLeft = panel.Left + 14.0f * scale;
		var slotTop = panel.Bottom - 22.0f * scale;

		for ( var index = 0; index < selectionCount; index++ )
		{
			if ( !player.TryGetInventorySelectionAt( index, out var selection ) ||
				!TryGetSelectionPresentation(
					player,
					selection,
					out _,
					out var color,
					out _,
					out _,
					out _ ) )
			{
				continue;
			}

			var selected =
				player.ActiveInventorySelection == selection;
			var slot = new Rect(
				railLeft + index * (slotWidth + gap),
				slotTop,
				slotWidth,
				14.0f * scale );
			DrawRoundedRect(
				hud,
				slot,
				selected ? color.WithAlpha( 0.26f ) : BarTrackColor,
				3.0f * scale );
			if ( selected )
			{
				DrawSolidRect(
					hud,
					new Rect(
						slot.Left,
						slot.Bottom - 2.0f * scale,
						slot.Width,
						2.0f * scale ),
					color );
			}

			var slotNumber = index == 9 ? "0" : $"{index + 1}";
			DrawHudText(
				hud,
				slotNumber,
				9.0f * scale,
				7.0f * scale,
				selected ? Color.White : MutedTextColor,
				slot,
				TextFlag.Center | TextFlag.SingleLine,
				BoldFontWeight );
		}
	}

	private static bool TryGetSelectionPresentation(
		LargeLadPlayer player,
		LargeLadInventorySelection selection,
		out string displayName,
		out Color color,
		out LargeLadWeaponState state,
		out bool isFirearm,
		out string tag )
	{
		displayName = string.Empty;
		color = MutedTextColor;
		state = default;
		isFirearm = false;
		tag = string.Empty;

		if ( selection.Kind == LargeLadInventorySelectionKind.RoleAbility )
		{
			var definition =
				LargeLadWeaponCatalog.Get( LargeLadWeaponId.Melee );
			displayName = definition.DisplayName;
			color = definition.PickupColor;
			tag = "ROLE";
			return true;
		}

		if ( selection.Kind == LargeLadInventorySelectionKind.Utility )
		{
			displayName =
				LargeLadUtilityRules.GetDisplayName( selection.Utility );
			color = LargeLadUtilityRules.GetColor( selection.Utility );
			tag = "UTILITY";
			return true;
		}

		if ( !player.TryGetFirearmForSelection( selection, out state ) )
			return false;

		var firearm = LargeLadWeaponCatalog.Get( state.Weapon );
		displayName = firearm.DisplayName;
		color = state.IsExclusive
			? new Color( 1.0f, 0.58f, 0.16f )
			: firearm.PickupColor;
		isFirearm = true;
		tag = state.IsExclusive ? "EXCLUSIVE" : string.Empty;
		return true;
	}

	private static void DrawBuiltInRoleAttackStatus(
		HudPainter hud,
		LargeLadRole role,
		float scale )
	{
		var accent = GetRoleColor( role );
		var label = role == LargeLadRole.LargeLad ? "EAT" : "MELEE";
		var panel = new Rect(
			Screen.Width - 324.0f * scale,
			Screen.Height - 88.0f * scale,
			300.0f * scale,
			64.0f * scale );

		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			label,
			19.0f * scale,
			14.0f * scale,
			accent,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Top,
				170.0f * scale,
				panel.Height ),
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );
		DrawHudText(
			hud,
			"PRIMARY",
			11.0f * scale,
			9.0f * scale,
			MutedTextColor,
			new Rect(
				panel.Right - 94.0f * scale,
				panel.Top,
				80.0f * scale,
				panel.Height ),
			TextFlag.RightCenter | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawGroundSlamFeedback(
		HudPainter hud,
		LargeLadPlayer player,
		float scale )
	{
		var presentation = player.GroundSlamPresentation;

		if ( player.Role != LargeLadRole.LargeLad ||
			player.Health?.IsDead != false ||
			presentation is null ||
			(!presentation.HasCooldownHud && !presentation.HasReadyHud) )
		{
			return;
		}

		var accent = presentation.HasReadyHud
			? new Color( 0.45f, 1.0f, 0.38f )
			: new Color( 1.0f, 0.48f, 0.12f );
		var panel = new Rect(
			Screen.Width - 324.0f * scale,
			Screen.Height - 128.0f * scale,
			300.0f * scale,
			32.0f * scale );
		var status = presentation.HasReadyHud
			? "READY"
			: FormatSeconds( presentation.CooldownRemaining );

		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			"GROUND SLAM",
			12.0f * scale,
			10.0f * scale,
			accent,
			new Rect(
				panel.Left + 14.0f * scale,
				panel.Top,
				170.0f * scale,
				panel.Height ),
			TextFlag.LeftCenter | TextFlag.SingleLine,
			BoldFontWeight );
		DrawHudText(
			hud,
			status,
			13.0f * scale,
			10.0f * scale,
			Color.White,
			new Rect(
				panel.Right - 90.0f * scale,
				panel.Top,
				76.0f * scale,
				panel.Height ),
			TextFlag.RightCenter | TextFlag.SingleLine,
			BoldFontWeight );
	}

	private static void DrawPickupFeedback(
		HudPainter hud,
		LargeLadPlayer player,
		float scale )
	{
		var message = player.NativeInventory?.HasPickupFeedback == true
			? player.NativeInventory.PickupFeedback
			: player.Inventory?.PickupFeedback;

		if ( string.IsNullOrWhiteSpace( message ) )
		{
			return;
		}

		var centerX = Screen.Width * 0.5f;
		var panel = new Rect(
			centerX - 210.0f * scale,
			Screen.Height * 0.68f,
			420.0f * scale,
			44.0f * scale );
		var accent = new Color( 1.0f, 0.58f, 0.16f );
		DrawPanel( hud, panel, accent, scale );
		DrawHudText(
			hud,
			message,
			15.0f * scale,
			12.0f * scale,
			Color.White,
			Inset( panel, 16.0f * scale, 6.0f * scale ),
			TextFlag.Center | TextFlag.WordWrap,
			RegularFontWeight );
	}

	private static void DrawCrosshairSegment(
		HudPainter hud,
		Rect rect,
		Color color,
		float scale )
	{
		var underlay = System.MathF.Max( 0.75f, scale );
		DrawSolidRect(
			hud,
			new Rect(
				rect.Left - underlay,
				rect.Top - underlay,
				rect.Width + underlay * 2.0f,
				rect.Height + underlay * 2.0f ),
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

	private static void DrawRoundedRect(
		HudPainter hud,
		Rect rect,
		Color color,
		float radius )
	{
		hud.DrawRect(
			rect,
			color,
			new Vector4( radius, radius, radius, radius ),
			new Vector4( 0.0f, 0.0f, 0.0f, 0.0f ),
			new Color( 0.0f, 0.0f, 0.0f, 0.0f ) );
	}

	private static void DrawBar(
		HudPainter hud,
		Rect rect,
		float fraction,
		Color color,
		float scale )
	{
		var radius = rect.Height * 0.5f;
		DrawRoundedRect( hud, rect, BarTrackColor, radius );

		var clampedFraction = System.MathF.Max(
			0.0f,
			System.MathF.Min( 1.0f, fraction ) );
		if ( clampedFraction <= 0.0f )
			return;

		var fill = new Rect(
			rect.Left,
			rect.Top,
			System.MathF.Max( 2.0f * scale, rect.Width * clampedFraction ),
			rect.Height );
		DrawRoundedRect( hud, fill, color, radius );
	}

	private static void DrawHudText(
		HudPainter hud,
		string text,
		float size,
		float minimumSize,
		Color color,
		Rect rect,
		TextFlag flags,
		int weight )
	{
		if ( string.IsNullOrWhiteSpace( text ) ||
			rect.Width <= 0.0f ||
			rect.Height <= 0.0f )
		{
			return;
		}

		var fittedSize = System.MathF.Max( minimumSize, size );
		var scope = CreateTextScope( text, color, fittedSize, weight );
		var shouldFitSingleLine = (flags & TextFlag.SingleLine) != 0;

		while ( shouldFitSingleLine &&
			fittedSize > minimumSize &&
			scope.Measure().x > rect.Width )
		{
			fittedSize = System.MathF.Max( minimumSize, fittedSize - 0.5f );
			scope = CreateTextScope( text, color, fittedSize, weight );
		}

		hud.DrawText( scope, rect, flags );
	}

	private static TextRendering.Scope CreateTextScope(
		string text,
		Color color,
		float size,
		int weight )
	{
		return new TextRendering.Scope(
			text,
			color,
			size,
			HudFont,
			weight )
		{
			LetterSpacing = weight >= BoldFontWeight ? 0.2f : 0.0f
		};
	}

	private static void DrawPanel(
		HudPainter hud,
		Rect rect,
		Color accent,
		float scale )
	{
		var cornerRadius = 3.0f * scale;
		var shadowOffset = 3.0f * scale;
		DrawRoundedRect(
			hud,
			new Rect(
				rect.Left + shadowOffset,
				rect.Top + shadowOffset,
				rect.Width,
				rect.Height ),
			accent.WithAlpha( 0.45f ),
			cornerRadius );
		DrawRoundedRect( hud, rect, PanelColor, cornerRadius );
	}

	private static Rect Inset( Rect rect, float horizontal, float vertical )
	{
		return new Rect(
			rect.Left + horizontal,
			rect.Top + vertical,
			System.MathF.Max( 0.0f, rect.Width - horizontal * 2.0f ),
			System.MathF.Max( 0.0f, rect.Height - vertical * 2.0f ) );
	}

	private static float GetHealthFraction( float current, float maximum )
	{
		if ( !float.IsFinite( current ) ||
			!float.IsFinite( maximum ) ||
			maximum <= 0.0f )
		{
			return 0.0f;
		}

		return System.MathF.Max(
			0.0f,
			System.MathF.Min( 1.0f, current / maximum ) );
	}

	private static float GetHudScale()
	{
		if ( Screen.Width <= 0.0f || Screen.Height <= 0.0f )
			return 1.0f;

		var widthScale = Screen.Width / ReferenceWidth;
		var heightScale = Screen.Height / ReferenceHeight;
		return System.MathF.Max(
			MinimumHudScale,
			System.MathF.Min(
				MaximumHudScale,
				System.MathF.Min( widthScale, heightScale ) ) );
	}

	private static float GetCrosshairScale( float hudScale )
	{
		return System.MathF.Max(
			0.85f,
			System.MathF.Min( 1.25f, hudScale ) );
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

	private static string FormatSeconds( float seconds )
	{
		return $"{System.MathF.Max( 0.0f, System.MathF.Ceiling( seconds ) ):0}s";
	}

	private static string FormatClock( float seconds )
	{
		var totalSeconds = (int)System.MathF.Max(
			0.0f,
			System.MathF.Ceiling( seconds ) );
		return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
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
