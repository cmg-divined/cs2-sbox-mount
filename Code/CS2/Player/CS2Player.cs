using System;

namespace CS2;

public enum CS2Team
{
	Random,
	CounterTerrorist,
	Terrorist,
}

/// <summary>
/// A CS2 player in a mounted map: spawns at one of its team's spawn points as the team's default agent, holding the
/// team's rifle (M4A4 for CT, AK-47 for T) and a knife, and moves, looks and handles its weapons as in the game, with
/// the game's own animation graphs for the first person arms and weapon (viewmodel.vnmgraph) and the third person
/// body (worldmodel.vnmgraph).
/// </summary>
/// <remarks>
/// Controls: WASD, Space to jump, Ctrl to crouch, Shift to walk, 3 for the rifle, 4 for the knife, Q for the last
/// weapon, F to inspect, mouse buttons to attack, R to reload, C for third person.
/// <para>
/// Ported or taken from the game: the weapons' numbers (scripts/weapons.vdata), the graphs and their parameters, the
/// movement constants, the hull and eye heights, the third person body state (ValveResourceFormat's CS2 player), recoil
/// and its view punch, viewmodel_offset_x/y/z (1, 1, -1) and viewmodel_fov 60. Not the game's: collision is s&amp;box's
/// character controller (sliding and stepping), and the view model bob and sway are ValveResourceFormat's.
/// </para>
/// </remarks>
[Title( "CS2 Player" ), Category( "Counter-Strike 2" ), Icon( "person" )]
public sealed partial class CS2Player : Component
{
	[Property] public CS2Team Team { get; set; } = CS2Team.Random;

	/// <summary>
	/// The agents the teams play as: the game's map based defaults (items_game customplayer_*_map_based).
	/// </summary>
	[Property, Group( "Loadout" )] public string CounterTerroristModel { get; set; } = "agents/models/ctm_sas/ctm_sas.vmdl";
	[Property, Group( "Loadout" )] public string TerroristModel { get; set; } = "agents/models/tm_phoenix/tm_phoenix.vmdl";

	/// <summary>
	/// The primary each team carries (key 3), and its finish: paint kit (items_game paint_kits name, e.g.
	/// cu_ak47_asiimov; empty for none), float (wear, 0 to 1, held to the paint kit's range), pattern seed and StatTrak.
	/// </summary>
	[Property, Group( "Primary" )] public CS2Gun CounterTerroristPrimary { get; set; } = CS2Gun.M4a1;
	[Property, Group( "Primary" )] public CS2Gun TerroristPrimary { get; set; } = CS2Gun.Ak47;
	[Property, Group( "Primary" ), CS2PaintKit( CS2PaintKitSlot.Primary )] public string PrimaryPaintKit { get; set; } = "";
	[Property, Group( "Primary" ), Range( 0f, 1f, clamped: true, slider: false )] public float PrimaryFloat { get; set; } = 0.01f;

	/// <summary>
	/// The primary's wear tier. As in the game, an item has only its float; its wear (Factory New to Battle-Scarred) is
	/// the range the float falls in. Picking a tier here sets the float to the middle of it; typing a float sets the tier.
	/// The float is a 32 bit float, as the game stores it (about 7 significant digits).
	/// </summary>
	[Property, Group( "Primary" )]
	public CS2Wear PrimaryWear
	{
		get => CS2LoadoutNames.WearOf( PrimaryFloat );
		set
		{
			if ( CS2LoadoutNames.WearOf( PrimaryFloat ) != value )
				PrimaryFloat = value.FloatOf();
		}
	}
	[Property, Group( "Primary" ), Range( 0, 1000 )] public int PrimarySeed { get; set; }
	[Property, Group( "Primary" )] public bool PrimaryStatTrak { get; set; }
	[Property, Group( "Primary" ), ShowIf( nameof( PrimaryStatTrak ), true )] public int PrimaryKills { get; set; }

	/// <summary>
	/// The knife (key 4), and its finish as the primary's.
	/// </summary>
	[Property, Group( "Knife" )] public CS2Knife Knife { get; set; } = CS2Knife.Karambit;
	[Property, Group( "Knife" ), CS2PaintKit( CS2PaintKitSlot.Knife )] public string KnifePaintKit { get; set; } = "";
	[Property, Group( "Knife" ), Range( 0f, 1f, clamped: true, slider: false )] public float KnifeFloat { get; set; } = 0.01f;

	[Property, Group( "Knife" )]
	public CS2Wear KnifeWear
	{
		get => CS2LoadoutNames.WearOf( KnifeFloat );
		set
		{
			if ( CS2LoadoutNames.WearOf( KnifeFloat ) != value )
				KnifeFloat = value.FloatOf();
		}
	}
	[Property, Group( "Knife" ), Range( 0, 1000 )] public int KnifeSeed { get; set; }
	[Property, Group( "Knife" )] public bool KnifeStatTrak { get; set; }
	[Property, Group( "Knife" ), ShowIf( nameof( KnifeStatTrak ), true )] public int KnifeKills { get; set; }

	/// <summary>
	/// The volume of every CS2 sound, and of its mix groups: weapons (shots, distant shots, explosions), foley
	/// (handling: magazines, bolts, knife swings), footsteps, and impacts (bullet impacts, physics, damage).
	/// </summary>
	[Property, Group( "Sound" ), Range( 0f, 1f )] public float Volume { get; set; } = 1f;
	[Property, Group( "Sound" ), Range( 0f, 1f )] public float WeaponVolume { get; set; } = 1f;
	[Property, Group( "Sound" ), Range( 0f, 1f )] public float FoleyVolume { get; set; } = 1f;
	[Property, Group( "Sound" ), Range( 0f, 1f )] public float FootstepVolume { get; set; } = 1f;
	[Property, Group( "Sound" ), Range( 0f, 1f )] public float ImpactVolume { get; set; } = 1f;

	private void ApplySoundVolumes()
	{
		CS2SoundEvent.MasterVolume = Volume;
		foreach ( var group in new[] { "Weapons", "WeaponsDistant", "Explosions", "ExplosionsDistant" } )
			CS2SoundEvent.SetVolume( group, WeaponVolume );

		CS2SoundEvent.SetVolume( "Foley", FoleyVolume );
		CS2SoundEvent.SetVolume( "Footsteps", FootstepVolume );
		foreach ( var group in new[] { "BulletImpacts", "Physics", "PlayerDamage" } )
			CS2SoundEvent.SetVolume( group, ImpactVolume );
	}

	/// <summary>
	/// The gloves (items_game hands_paintable name, e.g. sporty_gloves; empty for the agent's own) and their finish.
	/// </summary>
	[Property, Group( "Gloves" ), CS2Glove] public string Gloves { get; set; } = "";
	[Property, Group( "Gloves" ), CS2PaintKit( CS2PaintKitSlot.Gloves )] public string GlovesPaintKit { get; set; } = "";
	[Property, Group( "Gloves" ), Range( 0f, 1f, clamped: true, slider: false )] public float GlovesFloat { get; set; } = 0.01f;

	[Property, Group( "Gloves" )]
	public CS2Wear GlovesWear
	{
		get => CS2LoadoutNames.WearOf( GlovesFloat );
		set
		{
			if ( CS2LoadoutNames.WearOf( GlovesFloat ) != value )
				GlovesFloat = value.FloatOf();
		}
	}
	[Property, Group( "Gloves" ), Range( 0, 1000 )] public int GlovesSeed { get; set; }

	[Property, Group( "View" )] public bool ThirdPerson { get; set; }

	/// <summary>
	/// The game's field of view, 90 degrees across a 4:3 screen (wider screens see more to the sides).
	/// </summary>
	[Property, Group( "View" )] public float FieldOfView { get; set; } = 90;

	/// <summary>
	/// viewmodel_fov, and viewmodel_offset_x, _y and _z (right, forward, up).
	/// </summary>
	[Property, Group( "View" )] public float ViewmodelFieldOfView { get; set; } = 60;
	[Property, Group( "View" )] public Vector3 ViewmodelOffset { get; set; } = new( 1, 1, -1 );

	/// <summary>
	/// How much nearer the eye the view model is drawn, the same size on screen, so it never reaches into walls: the
	/// game draws it in a pass of its own, which s&amp;box's pipeline doesn't have. At a quarter, the furthest of a gun
	/// is under ten units out, inside the player's hull.
	/// </summary>
	[Property, Group( "View" ), Range( 0.05f, 1f )] public float ViewmodelDepthScale { get; set; } = 0.25f;

	public CS2Team CurrentTeam { get; private set; }
	public Angles EyeAngles { get; set; }
	public CS2Weapon ActiveWeapon { get; private set; }
	public CS2Weapon PrimaryWeapon { get; private set; }
	public CS2Weapon KnifeWeapon { get; private set; }

	// Movement (ValveResourceFormat's CS2 player, sv_* defaults)
	private const float Gravity = 800f;
	private const float Friction = 5.2f;
	private const float StopSpeed = 80f;
	private const float Accelerate = 5.5f;
	private const float AirAccelerate = 12f;
	private const float AirMaxWishSpeed = 30f;
	private const float JumpImpulse = 301.993377f;
	private const float MaxVelocity = 3500f;
	private const float WalkSpeedModifier = 0.52f;
	private const float DuckSpeedModifier = 0.34f;
	private const float StandingHeight = 72f;
	private const float DuckedHeight = 48f;
	private const float StandingEyeHeight = 64f;
	private const float DuckedEyeHeight = 46f;
	private const float CrouchBlendTime = 0.2f;

	private CharacterController controller;
	private SkinnedModelRenderer body;
	private SkinnedModelRenderer legs;
	private SkinnedModelRenderer bodyWeapon;
	private CS2Animator bodyAnimator;
	private GameObject viewmodelRoot;
	private SkinnedModelRenderer arms;
	private SkinnedModelRenderer armsWeapon;
	private CS2Animator armsAnimator;

	private readonly CS2PlayerAnimationState animationState = new();
	private readonly CS2AimPunch aimPunch = new();
	private readonly Dictionary<CS2Weapon, (int Clip, int Reserve)> ammo = [];

	private float crouchBlend;
	private bool jumped;
	private bool wasOnGround = true;

	// The weapon's action, as both graphs are told it: action_deploy, action_attack, action_inspect, action_reload,
	// action_idle; the clip it started; how long it's been going.
	private string action = "action_idle";
	private string attackType = "";
	private string actionClip;
	private float actionTime;
	private bool actionRestarted;
	private float deployTimeLeft;
	private float attackCooldown;
	private float recoilIndex;
	private float lastShotTime = float.NegativeInfinity;
	private int knifeSwing;
	private CS2Weapon previousWeapon;
	private readonly Dictionary<string, float> clipTimes = new( StringComparer.OrdinalIgnoreCase );

	// View model sway and bob (ValveResourceFormat's CS2 player).
	private Vector3 currentBob;
	private readonly List<(float Time, float Yaw)> yawHistory = [];

	protected override void OnStart()
	{
		CurrentTeam = Team != CS2Team.Random ? Team : Game.Random.Int( 0, 1 ) == 0 ? CS2Team.CounterTerrorist : CS2Team.Terrorist;

		MoveToSpawn();
		Build();
		UpdateLoadout();
	}

	// The finish last painted on the weapon in hand, and when: a float or seed dragged in the inspector repaints at
	// most every tenth of a second (each repaint generates new textures).
	private (CS2Weapon Weapon, string PaintKit, float Float, int Seed)? appliedFinish;
	private (string Gloves, string PaintKit, float Float, int Seed)? appliedGloves;
	private (CS2Weapon Weapon, bool StatTrak, int Kills)? appliedStatTrak;
	private RealTimeSince sinceFinishApplied;

	/// <summary>
	/// Paints both renderers of the weapon in hand with its slot's finish (CS2WeaponSkin) when the weapon or the
	/// finish changed, puts on the gloves (CS2Gloves) when they changed, and shows the slot's StatTrak module on the
	/// viewmodel (CS2StatTrak; on the viewmodel only, as CS:GO did; unverified for CS2's third person).
	/// </summary>
	private void ApplyFinishes()
	{
		// The gloves on both the body and the arms (CS2Gloves).
		var gloves = (Gloves, GlovesPaintKit, GlovesFloat, GlovesSeed);
		if ( body is not null && arms is not null && appliedGloves != gloves && (appliedGloves?.Gloves != gloves.Gloves || sinceFinishApplied >= 0.1f) )
		{
			appliedGloves = gloves;
			sinceFinishApplied = 0;
			CS2Gloves.Apply( body, false, gloves.Gloves, gloves.GlovesPaintKit, gloves.GlovesFloat, gloves.GlovesSeed );
			CS2Gloves.Apply( arms, true, gloves.Gloves, gloves.GlovesPaintKit, gloves.GlovesFloat, gloves.GlovesSeed );
		}

		if ( ActiveWeapon is null || bodyWeapon is null || armsWeapon is null )
			return;

		var knife = ActiveWeapon == KnifeWeapon;
		var finish = (ActiveWeapon, knife ? KnifePaintKit : PrimaryPaintKit, knife ? KnifeFloat : PrimaryFloat, knife ? KnifeSeed : PrimarySeed);

		if ( appliedFinish != finish && (appliedFinish?.Weapon != finish.ActiveWeapon || sinceFinishApplied >= 0.1f) )
		{
			appliedFinish = finish;
			sinceFinishApplied = 0;
			CS2WeaponSkin.Apply( bodyWeapon, finish.Item2, finish.Item3, finish.Item4 );
			CS2WeaponSkin.Apply( armsWeapon, finish.Item2, finish.Item3, finish.Item4 );
			appliedStatTrak = null;
		}

		var statTrak = (ActiveWeapon, knife ? KnifeStatTrak : PrimaryStatTrak, knife ? KnifeKills : PrimaryKills);
		if ( appliedStatTrak != statTrak )
		{
			appliedStatTrak = statTrak;
			CS2StatTrak.Apply( armsWeapon, statTrak.Item2, statTrak.Item3 );
		}
	}

	// The loadout last equipped, so changes made to it in the inspector while playing take effect.
	private (CS2Gun CounterTerrorist, CS2Gun Terrorist, CS2Knife Knife)? equippedLoadout;

	/// <summary>
	/// Equips the team's primary and the knife when they've changed, keeping the slot in hand: a new primary or knife
	/// is drawn if it replaces the one held, with full ammo.
	/// </summary>
	private void UpdateLoadout()
	{
		var loadout = (CounterTerroristPrimary, TerroristPrimary, Knife);
		if ( equippedLoadout == loadout )
			return;

		equippedLoadout = loadout;

		var primary = CS2Weapon.Find( (CurrentTeam == CS2Team.CounterTerrorist ? CounterTerroristPrimary : TerroristPrimary).WeaponName() )
			?? CS2Weapon.Find( CurrentTeam == CS2Team.CounterTerrorist ? "weapon_m4a1" : "weapon_ak47" );
		var knife = CS2Weapon.Find( Knife.WeaponName( CurrentTeam ) ) ?? CS2Weapon.Find( CS2Knife.Default.WeaponName( CurrentTeam ) );
		var holdingKnife = ActiveWeapon is not null && ActiveWeapon == KnifeWeapon;

		foreach ( var weapon in new[] { primary, knife } )
		{
			if ( weapon is not null && !ammo.ContainsKey( weapon ) )
				ammo[weapon] = (weapon.ClipSize, weapon.ReserveAmmo);
		}

		PrimaryWeapon = primary;
		KnifeWeapon = knife;

		if ( previousWeapon is not null && previousWeapon != primary && previousWeapon != knife )
			previousWeapon = null;

		SelectWeapon( holdingKnife ? knife : primary );
	}

	/// <summary>
	/// One of the team's spawn points in the map, at random; where the player is otherwise.
	/// </summary>
	private void MoveToSpawn()
	{
		var classname = CurrentTeam == CS2Team.CounterTerrorist ? "info_player_counterterrorist" : "info_player_terrorist";
		var spawns = Scene.GetAllComponents<CS2Entity>()
			.Where( x => string.Equals( x.Classname, classname, StringComparison.OrdinalIgnoreCase ) && x.Active )
			.ToList();

		if ( spawns.Count == 0 )
		{
			EyeAngles = WorldRotation.Angles().WithPitch( 0 ).WithRoll( 0 );
			return;
		}

		var spawn = spawns[Game.Random.Int( 0, spawns.Count - 1 )];
		WorldPosition = spawn.WorldPosition;
		WorldRotation = Rotation.Identity;
		EyeAngles = new Angles( 0, spawn.WorldRotation.Yaw(), 0 );
	}

	private void Build()
	{
		controller = Components.GetOrCreate<CharacterController>();
		controller.Radius = 16;
		controller.Height = StandingHeight;
		controller.StepHeight = 18;
		controller.GroundAngle = 45.57f;
		// What a player goes through: itself, grenade clips, water, and what the map's collision says excludes players.
		// Player clips (tagged npcclip and playerclip) and pass-bullets brushes stop it.
		foreach ( var tag in new[] { "player", "exclude_player", "csgo_grenadeclip", "water", "trigger" } )
			controller.IgnoreLayers.Add( tag );

		GameObject.Tags.Add( "player" );

		var agent = LoadModel( CurrentTeam == CS2Team.CounterTerrorist ? CounterTerroristModel : TerroristModel );

		// Third person: the agent's body, the worldmodel graph, the weapon in its hand.
		var bodyObject = new GameObject( GameObject, true, "Body" );
		body = bodyObject.Components.Create<SkinnedModelRenderer>();
		body.Model = agent;
		body.SetCS2BodyGroup( "first_or_third_person", "thirdperson_default" );

		var bodyWeaponObject = new GameObject( bodyObject, true, "Weapon" );
		bodyWeapon = bodyWeaponObject.Components.Create<SkinnedModelRenderer>();

		// First person: the body's own legs, its upper body collapsed, bent out of view looking down by the character
		// shader's first person legs distortion.
		var legsObject = new GameObject( bodyObject, true, "Legs" );
		legs = legsObject.Components.Create<SkinnedModelRenderer>();
		legs.Model = agent;
		legs.SetCS2BodyGroup( "first_or_third_person", "thirdperson_default" );
		legs.RenderType = ModelRenderer.ShadowRenderType.Off;
		legs.Attributes.Set( "CS2FirstpersonLegsDistortion", 1f );

		bodyAnimator = bodyObject.Components.Create<CS2Animator>();
		bodyAnimator.Renderer = body;
		bodyAnimator.Weapon = bodyWeapon;
		bodyAnimator.FirstPersonLegs = legs;
		bodyAnimator.UpdateAutomatically = false;

		// First person: the agent's arms, the viewmodel graph, the weapon in hand, drawn over the world.
		viewmodelRoot = new GameObject( GameObject, true, "Viewmodel" );
		arms = viewmodelRoot.Components.Create<SkinnedModelRenderer>();
		arms.Model = agent;
		arms.SetCS2BodyGroup( "first_or_third_person", "firstperson_default" );
		arms.RenderType = ModelRenderer.ShadowRenderType.Off;

		var armsWeaponObject = new GameObject( viewmodelRoot, true, "Weapon" );
		armsWeapon = armsWeaponObject.Components.Create<SkinnedModelRenderer>();
		armsWeapon.RenderType = ModelRenderer.ShadowRenderType.Off;

		armsAnimator = viewmodelRoot.Components.Create<CS2Animator>();
		armsAnimator.Renderer = arms;
		armsAnimator.Weapon = armsWeapon;
		armsAnimator.GraphIdentifier = "hudmodel";
		armsAnimator.UpdateAutomatically = false;
		armsAnimator.RecordSampledClips = true;
	}

	/// <summary>
	/// Draws a weapon: both graphs play its deploy.
	/// </summary>
	public void SelectWeapon( CS2Weapon weapon )
	{
		if ( weapon is null || weapon == ActiveWeapon )
			return;

		previousWeapon = ActiveWeapon;
		ActiveWeapon = weapon;

		var model = LoadModel( weapon.Model );
		bodyWeapon.Model = model;
		armsWeapon.Model = model;
		ApplyFinishes();

		deployTimeLeft = weapon.DeployDuration;
		recoilIndex = 0;
		StartAction( "action_deploy" );
	}

	private void StartAction( string newAction, string newAttackType = "" )
	{
		action = newAction;
		attackType = newAttackType;
		actionClip = null;
		actionTime = 0;
		actionRestarted = true;
	}

	protected override void OnUpdate()
	{
		if ( controller is null )
			return;

		var dt = Time.Delta;
		ApplySoundVolumes();
		UpdateLoadout();
		ApplyFinishes();

		Look();
		Weapons( dt );
		Move( dt );
		aimPunch.Update( dt );

		if ( Input.Pressed( "View" ) )
			ThirdPerson = !ThirdPerson;

		AnimateBody( dt );
		PlaceView( dt );
		AnimateArms( dt );
	}

	private void Look()
	{
		var angles = EyeAngles + Input.AnalogLook;
		angles.pitch = Math.Clamp( angles.pitch, -89f, 89f );
		angles.roll = 0;
		EyeAngles = angles;
	}

	private void Weapons( float dt )
	{
		attackCooldown = MathF.Max( 0, attackCooldown - dt );
		deployTimeLeft = MathF.Max( 0, deployTimeLeft - dt );
		actionTime += dt;
		DecayRecoilIndex( dt );

		if ( Input.Pressed( "Slot3" ) )
			SelectWeapon( PrimaryWeapon );
		else if ( Input.Pressed( "Slot4" ) )
			SelectWeapon( KnifeWeapon );
		else if ( Input.Pressed( "Menu" ) )
			SelectWeapon( previousWeapon );

		var weapon = ActiveWeapon;
		if ( weapon.IsKnife && (Input.Down( "Attack1" ) || Input.Down( "Attack2" )) )
			Attack( secondary: !Input.Down( "Attack1" ) );
		else if ( !weapon.IsKnife && (weapon.FullAuto ? Input.Down( "Attack1" ) : Input.Pressed( "Attack1" )) )
			Attack( secondary: false );
		else if ( weapon.DetachableSilencer && Input.Pressed( "Attack2" ) )
			ToggleSilencer();

		if ( Input.Pressed( "Reload" ) )
			Reload();

		if ( Input.Keyboard.Pressed( "f" ) )
			Inspect();
	}

	private bool IsDeployed => deployTimeLeft <= 0;

	// The weapons whose detachable silencer has been taken off; they're carried with it on.
	private readonly HashSet<CS2Weapon> silencerOff = [];

	private bool IsSilenced( CS2Weapon weapon ) => weapon.DetachableSilencer && !silencerOff.Contains( weapon );

	/// <summary>
	/// Screws the silencer of an M4A1-S or USP-S off, or back on (the secondary attack).
	/// </summary>
	public void ToggleSilencer()
	{
		var weapon = ActiveWeapon;
		if ( weapon is { DetachableSilencer: true } && IsDeployed && action is "action_idle" or "action_inspect" )
			StartAction( IsSilenced( weapon ) ? "action_silencer_detach" : "action_silencer_attach" );
	}

	/// <summary>
	/// Swings the knife (light, or heavy for the secondary attack) or fires the gun, when it's ready; an empty gun
	/// reloads instead.
	/// </summary>
	public void Attack( bool secondary )
	{
		var weapon = ActiveWeapon;
		if ( weapon is null || !IsDeployed || attackCooldown > 0 || action is "action_reload" or "action_silencer_attach" or "action_silencer_detach" )
			return;

		if ( weapon.IsKnife )
		{
			StartAction( "action_attack", secondary ? "attack_knife_heavymiss" : "attack_knife_lightmiss" );
			armsAnimator.SetFloat( "attack_variation", secondary ? 0 : knifeSwing++ % 2 );
			attackCooldown = secondary ? weapon.SecondaryCycleTime : weapon.CycleTime;
			return;
		}

		var (clip, reserve) = ammo.GetValueOrDefault( weapon );
		if ( clip > 0 )
		{
			StartAction( "action_attack", "attack_gun_primaryfire" );
			CS2SoundEvent.Play( IsSilenced( weapon ) ? weapon.SilencedShootSound ?? weapon.ShootSound : weapon.ShootSound, WorldPosition );
			ammo[weapon] = (clip - 1, reserve);
			attackCooldown = weapon.CycleTime;
			FireBullet();
			Recoil( weapon );
		}
		else if ( reserve > 0 )
		{
			StartAction( "action_reload" );
		}
	}

	/// <summary>
	/// Reloads the gun when its clip isn't full and there's ammo to fill it. A weapon that can't reload stops its
	/// inspect instead, so the next inspect starts over: pressing inspect and reload in turn (F R F R) replays the
	/// inspect's opening, as players do with the knives in the game (behaviour as the user described it).
	/// </summary>
	public void Reload()
	{
		var weapon = ActiveWeapon;
		if ( weapon is null || !IsDeployed || action == "action_reload" )
			return;

		var (clip, reserve) = ammo.GetValueOrDefault( weapon );
		if ( !weapon.IsKnife && clip < weapon.ClipSize && reserve > 0 )
			StartAction( "action_reload" );
		else if ( action == "action_inspect" )
			StartAction( "action_idle" );
	}

	/// <summary>
	/// Inspects the weapon in hand. One inspect under way holds off another until it's halfway through.
	/// </summary>
	public void Inspect()
	{
		if ( IsDeployed && action is "action_idle" or "action_inspect" && (action != "action_inspect" || ActionProgress() >= 0.5f) )
			StartAction( "action_inspect" );
	}

	private void Recoil( CS2Weapon weapon )
	{
		if ( weapon.Recoil is null )
			return;

		var (angle, magnitude) = weapon.Recoil[0, (int)recoilIndex];
		var viewPunch = aimPunch.Kick( angle, magnitude );
		EyeAngles += new Angles( viewPunch.X, viewPunch.Y, 0 );
		recoilIndex++;
		lastShotTime = Time.Now;
	}

	// Once a tick past the cycle time since the last shot, the recoil index falls to a hundredth of itself per second.
	private void DecayRecoilIndex( float dt )
	{
		if ( recoilIndex <= 0 || ActiveWeapon is null || Time.Now <= lastShotTime + ActiveWeapon.CycleTime + 1f / 64f )
			return;

		recoilIndex *= MathF.Pow( 0.01f, dt );
		if ( recoilIndex <= 0.1f )
			recoilIndex = 0;
	}

	private void Move( float dt )
	{
		var cc = controller;
		var ducking = Input.Down( "Duck" );
		var walking = Input.Down( "Run" ) && !ducking;

		// Crouching takes its time; standing up needs the room.
		var goal = ducking ? 1f : 0f;
		if ( goal < crouchBlend && !CanStand() )
			goal = crouchBlend;
		crouchBlend = crouchBlend < goal ? MathF.Min( goal, crouchBlend + dt / CrouchBlendTime ) : MathF.Max( goal, crouchBlend - dt / CrouchBlendTime );
		cc.Height = MathX.Lerp( StandingHeight, DuckedHeight, crouchBlend );

		var move = Input.AnalogMove;
		var wish = Rotation.FromYaw( EyeAngles.yaw ) * new Vector3( move.x, move.y, 0 );
		var wishDirection = wish.Normal;
		var maxSpeed = ActiveWeapon.MaxSpeed * (walking ? WalkSpeedModifier : 1f) * MathX.Lerp( 1f, DuckSpeedModifier, crouchBlend );
		var wishSpeed = wish.Length > 0 ? maxSpeed * MathF.Min( 1f, wish.Length ) : 0f;

		var velocity = cc.Velocity;
		jumped = false;

		if ( cc.IsOnGround )
		{
			if ( Input.Pressed( "Jump" ) )
			{
				jumped = true;
			}
			else
			{
				// Friction, then acceleration towards the wish.
				var speed = velocity.Length;
				if ( speed > 0.1f )
				{
					var drop = MathF.Max( speed, StopSpeed ) * Friction * dt;
					velocity *= MathF.Max( speed - drop, 0f ) / speed;
				}

				var add = wishSpeed - Vector3.Dot( velocity, wishDirection );
				if ( add > 0 )
					velocity += wishDirection * MathF.Min( Accelerate * dt * wishSpeed, add );

				velocity = velocity.WithZ( 0 );
			}
		}
		else
		{
			var add = MathF.Min( wishSpeed, AirMaxWishSpeed ) - Vector3.Dot( velocity, wishDirection );
			if ( add > 0 )
				velocity += wishDirection * MathF.Min( AirAccelerate * wishSpeed * dt, add );

			velocity -= Vector3.Up * Gravity * dt;
		}

		if ( velocity.Length > MaxVelocity )
			velocity = velocity.Normal * MaxVelocity;

		cc.Velocity = velocity;

		if ( jumped )
		{
			JumpSounds( cc.Velocity );
			cc.Punch( Vector3.Up * JumpImpulse );
		}

		var fallSpeed = -cc.Velocity.z;
		cc.Move();

		if ( cc.IsOnGround && !wasOnGround )
			LandSounds( fallSpeed );

		StepSounds( dt, walking, ducking );
		wasOnGround = cc.IsOnGround;
	}

	private bool CanStand()
	{
		var start = WorldPosition;
		var trace = Scene.Trace.Box( new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, StandingHeight ) ), start, start + Vector3.Up * 0.1f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( controller.IgnoreLayers.ToArray() )
			.Run();

		return !trace.StartedSolid;
	}

	private float EyeHeight => MathX.Lerp( StandingEyeHeight, DuckedEyeHeight, crouchBlend );

	/// <summary>
	/// The third person body: the state of its movement for the worldmodel graph, the weapon and its action.
	/// </summary>
	private void AnimateBody( float dt )
	{
		var velocity = controller.Velocity;
		var move = Input.AnalogMove;
		var punch = aimPunch.Punch;

		animationState.Update( new CS2PlayerAnimationState.Input
		{
			AimYaw = EyeAngles.yaw + punch.Y,
			AimPitch = EyeAngles.pitch + punch.X,
			Velocity = new System.Numerics.Vector3( velocity.x, velocity.y, velocity.z ),
			WishMove = new System.Numerics.Vector2( move.x, move.y ),
			MaxSpeed = ActiveWeapon.MaxSpeed,
			OnGround = controller.IsOnGround,
			Jumped = jumped,
		}, dt );

		var a = bodyAnimator;
		a.SetId( "move_type", MoveTypeNames[(int)animationState.CurrentMoveType] );
		a.SetId( "air_action", AirActionNames[(int)animationState.CurrentAirAction] );
		a.SetId( "move_direction_id", DirectionNames[(int)animationState.MoveDirection] );
		a.SetFloat( "move_speed_x", animationState.LocalVelocity.X );
		a.SetFloat( "move_speed_y", animationState.LocalVelocity.Y );
		a.SetFloat( "move_speed_horizontal", animationState.HorizontalSpeed );
		a.SetFloat( "move_speed_horizontal_previous", animationState.PreviousHorizontalSpeed );
		a.SetFloat( "move_crouch_amount", crouchBlend );
		a.SetBool( "move_is_walking", animationState.HorizontalSpeed > 0 && Input.Down( "Run" ) && !Input.Down( "Duck" ) );
		a.SetFloat( "aim_angle_pitch", animationState.AimPitch );
		a.SetFloat( "aim_angle_yaw", animationState.AimYaw );

		var heightAboveGround = 0f;
		if ( animationState.CurrentMoveType == CS2PlayerAnimationState.MoveType.Ground )
		{
			a.SetId( "ground_action", GroundActionNames[(int)animationState.GroundState] );
			a.SetId( "ground_action_direction_id", DirectionNames[(int)animationState.GroundActionDirection] );
			a.SetFloat( "ground_turn_angle_or_velocity", animationState.GroundState switch
			{
				CS2PlayerAnimationState.GroundMoveState.TurnOnSpot => animationState.TurnOnSpotAngle,
				CS2PlayerAnimationState.GroundMoveState.TurnOnSpotLoop => animationState.TurnVelocity,
				_ => 0f,
			} );
		}
		else if ( animationState.CurrentMoveType == CS2PlayerAnimationState.MoveType.Air )
		{
			var feet = WorldPosition;
			var ground = Scene.Trace.Ray( feet, feet - Vector3.Up * 72 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( controller.IgnoreLayers.ToArray() ).Run();
			heightAboveGround = ground.Hit ? feet.z - ground.HitPosition.z : 72f;
		}

		a.SetFloat( "air_height_above_ground", heightAboveGround );

		SetWeaponParameters( a );
		a.Step( dt );

		body.GameObject.WorldRotation = Rotation.FromYaw( animationState.BodyYaw );
		body.RenderType = ThirdPerson ? ModelRenderer.ShadowRenderType.On : ModelRenderer.ShadowRenderType.ShadowsOnly;
		bodyWeapon.RenderType = body.RenderType;
		legs.Enabled = !ThirdPerson;
	}

	private void SetWeaponParameters( CS2Animator animator )
	{
		var weapon = ActiveWeapon;
		var (clip, reserve) = ammo.GetValueOrDefault( weapon );

		animator.SetId( "weapon_category", weapon.Category );
		animator.SetId( "weapon_type", weapon.Name );
		animator.SetBool( "weapon_is_silenced", IsSilenced( weapon ) );
		animator.SetFloat( "weapon_ammo", clip );
		animator.SetFloat( "weapon_ammo_max", weapon.ClipSize );
		animator.SetFloat( "weapon_ammo_reserve", reserve );
		animator.SetFloat( "weapon_action_speedscale", 1f );
		animator.SetId( "action", action );
		animator.SetId( "attack_type", attackType );
	}

	/// <summary>
	/// The camera at the eyes (or behind the player), and the view model with it.
	/// </summary>
	private void PlaceView( float dt )
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() )
			return;

		var punch = aimPunch.Punch;
		var eye = WorldPosition + Vector3.Up * EyeHeight;
		var viewAngles = EyeAngles + new Angles( punch.X * CS2AimPunch.ViewScale, punch.Y * CS2AimPunch.ViewScale, 0 );

		camera.FovAxis = CameraComponent.Axis.Vertical;
		camera.FieldOfView = VerticalFieldOfView( FieldOfView );

		if ( ThirdPerson )
		{
			var rotation = EyeAngles.ToRotation();
			var behind = Scene.Trace.Ray( eye, eye - rotation.Forward * 100f ).Radius( 4f ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( controller.IgnoreLayers.ToArray() ).Run();
			camera.WorldPosition = behind.EndPosition;
			camera.WorldRotation = rotation;
			arms.Enabled = false;
			armsWeapon.Enabled = false;
			return;
		}

		camera.WorldPosition = eye;
		camera.WorldRotation = viewAngles.ToRotation();

		// The view model's nearest parts come within a unit of the eye.
		camera.ZNear = 1f;
		arms.Enabled = true;
		armsWeapon.Enabled = true;

		// The view model is drawn with its own field of view: across the view it is stretched by how much wider the
		// world's is, which is how a point on it lands on screen with the narrower one.
		var stretch = MathF.Tan( FieldOfView * MathF.PI / 360f ) / MathF.Tan( ViewmodelFieldOfView * MathF.PI / 360f );

		// It turns with the punched view and further still; it trails the view as it turns and bobs as the player moves.
		var modelAngles = EyeAngles + new Angles( punch.X * CS2AimPunch.ViewmodelScale, punch.Y * CS2AimPunch.ViewmodelScale, 0 );
		var rotationOfModel = modelAngles.ToRotation();
		var offset = new Vector3( ViewmodelOffset.y, -ViewmodelOffset.x, ViewmodelOffset.z ) - Bob( rotationOfModel ) + Sway();
		var depth = ViewmodelDepthScale;
		var scale = new Vector3( depth, depth * stretch, depth * stretch );

		viewmodelRoot.WorldTransform = new Transform( eye + rotationOfModel * (offset * scale), rotationOfModel, scale );
		LightViewmodelUnsqueezed( eye, rotationOfModel, scale );
	}

	/// <summary>
	/// Has the view model lit where the game draws it, at its true size in front of the eye, rather than squeezed into
	/// the head as it's drawn here: there it would sit in the body's shadow, and the squeeze would tilt its normals. A
	/// point drawn at p is truly at eye + A (p - eye), with A = R S^-1 R^T undoing the squeeze along the view model's
	/// axes; normals map by its inverse transpose, R S R^T.
	/// </summary>
	private void LightViewmodelUnsqueezed( Vector3 eye, Rotation rotation, Vector3 scale )
	{
		var axes = new[] { rotation.Forward, rotation.Left, rotation.Up };
		var scales = new[] { scale.x, scale.y, scale.z };

		static float Component( Vector3 v, int i ) => i switch { 0 => v.x, 1 => v.y, _ => v.z };

		for ( var row = 0; row < 3; row++ )
		{
			var direction = Vector3.Zero;
			var normal = Vector3.Zero;
			for ( var k = 0; k < 3; k++ )
			{
				direction += axes[k] * (Component( axes[k], row ) / scales[k]);
				normal += axes[k] * (Component( axes[k], row ) * scales[k]);
			}

			var transform = new Vector4( direction, Component( eye, row ) - Vector3.Dot( direction, eye ) );
			foreach ( var renderer in new ModelRenderer[] { arms, armsWeapon } )
			{
				renderer.Attributes.Set( $"CS2LightingTransform{row}", transform );
				renderer.Attributes.Set( $"CS2LightingNormal{row}", normal );
			}
		}
	}

	/// <summary>
	/// How far the view model lags the player's movement, and its walking bob (ValveResourceFormat's CS2 player), in its
	/// own space (forward, left, up).
	/// </summary>
	private Vector3 Bob( Rotation rotation )
	{
		const float referenceSpeed = 800f;
		const float overshoot = 0.15f * referenceSpeed;

		var velocity = controller.Velocity;
		var speed = velocity.Length;
		var bobSpeed = speed <= referenceSpeed ? speed : referenceSpeed + overshoot * (1f - MathF.Exp( -(speed - referenceSpeed) / overshoot ));
		var bobVelocity = speed > 1e-4f ? velocity * (bobSpeed / speed) : Vector3.Zero;

		var target = rotation.Inverse * (bobVelocity * 0.005f);
		target = new Vector3( target.x, -target.y * 0.3f, MathF.Abs( target.z ) * 0.3f );
		currentBob = Vector3.Lerp( currentBob, target, 0.5f );

		var amplitude = controller.IsOnGround ? Math.Clamp( (speed - 150f) / 150f, 0f, 1f ) * 0.1f : 0f;
		var walkBob = new Vector3( 1, 0.5f, 1 ) * MathF.Sin( Time.Now * 18f ) * amplitude;

		return currentBob + walkBob;
	}

	/// <summary>
	/// The view model trailing the view as it turns: cl_wpn_sway_interp 0.1 seconds behind, cl_wpn_sway_scale 0.32
	/// (ValveResourceFormat's CS2 player).
	/// </summary>
	private Vector3 Sway()
	{
		const float interp = 0.1f;
		const float scale = 0.32f;

		var now = Time.Now;
		var yaw = EyeAngles.yaw * MathF.PI / 180f;
		yawHistory.Add( (now, yaw) );
		yawHistory.RemoveAll( x => x.Time < now - 1f );

		var past = yawHistory[0].Yaw;
		for ( var i = yawHistory.Count - 1; i > 0; i-- )
		{
			if ( yawHistory[i - 1].Time <= now - interp )
			{
				var (olderTime, olderYaw) = yawHistory[i - 1];
				var (newerTime, newerYaw) = yawHistory[i];
				var t = newerTime > olderTime ? (now - interp - olderTime) / (newerTime - olderTime) : 0f;
				past = olderYaw + MathF.IEEERemainder( newerYaw - olderYaw, MathF.Tau ) * t;
				break;
			}
		}

		var delta = MathF.IEEERemainder( yaw - past, MathF.Tau );
		return new Vector3( 1f - MathF.Cos( delta ), -MathF.Sin( delta ), 0f ) * scale;
	}

	/// <summary>
	/// The first person arms and weapon: the viewmodel graph, the action back to idle once its clip has played.
	/// </summary>
	private void AnimateArms( float dt )
	{
		var a = armsAnimator;
		SetWeaponParameters( a );
		a.SetFloat( "deploy_variation", 0 );
		a.SetFloat( "inspect_variation", 0 );
		a.SetFloat( "idle_variation", 0 );
		a.SetFloat( "weapon_ironsight_amount", 0 );

		if ( actionRestarted )
		{
			a.Signal( "action_reset" );
			bodyAnimator.Signal( "action_reset" );
			actionRestarted = false;
		}

		a.Step( dt );
		AnimationSounds( a, WorldPosition + Vector3.Up * EyeHeight );

		var clips = a.SampledClips.ToList();
		var previousTimes = clipTimes.ToDictionary( x => x.Key, x => x.Value );
		clipTimes.Clear();
		foreach ( var sampled in clips )
			clipTimes[sampled.Clip] = sampled.Time;

		if ( action == "action_idle" )
			return;

		// The action's clip is the one the graph started for it (new, or back at its start), the longest if several;
		// when it has played through, back to idle (the graph holds an action's last frame otherwise). A reload refills
		// the clip then.
		if ( actionClip is null )
		{
			actionClip = clips
				.Where( x => !previousTimes.TryGetValue( x.Clip, out var before ) || x.Time < before )
				.OrderByDescending( x => x.Duration )
				.Select( x => x.Clip )
				.FirstOrDefault();
		}

		var current = clips.FirstOrDefault( x => x.Clip == actionClip );
		var finished = actionClip is not null && (current.Clip is null || current.Time >= current.Duration - 1e-3f);
		if ( !finished && actionTime < 10f )
			return;

		// The silencer is on or off once screwing it on or off has played through.
		if ( action is "action_silencer_attach" or "action_silencer_detach" && !silencerOff.Remove( ActiveWeapon ) )
			silencerOff.Add( ActiveWeapon );

		if ( action == "action_reload" )
		{
			var weapon = ActiveWeapon;
			var (clip, reserve) = ammo.GetValueOrDefault( weapon );
			var taken = Math.Min( weapon.ClipSize - clip, reserve );
			ammo[weapon] = (clip + taken, reserve - taken);
		}

		action = "action_idle";
		attackType = "";
		actionClip = null;
	}

	private float ActionProgress()
	{
		var current = armsAnimator.SampledClips.FirstOrDefault( x => x.Clip == actionClip );
		return current.Clip is null || current.Duration <= 0 ? 1f : current.Time / current.Duration;
	}

	/// <summary>
	/// A mounted CS2 model by the path the game names it by.
	/// </summary>
	private static Model LoadModel( string path )
		=> Model.Load( path.StartsWith( "mount://" ) ? path : $"mount://cs2/{path}" );

	/// <summary>
	/// A field of view across a 4:3 screen as the vertical one, which is the same on every screen.
	/// </summary>
	private static float VerticalFieldOfView( float horizontal43 )
		=> 2f * MathF.Atan( MathF.Tan( horizontal43 * MathF.PI / 360f ) * 0.75f ) * 180f / MathF.PI;

	// The names the worldmodel graph knows the body's states by, indexed by CS2PlayerAnimationState's enums.
	private static readonly string[] MoveTypeNames = ["", "move_type_ground", "move_type_air", "move_type_ladder"];
	private static readonly string[] GroundActionNames = ["", "ground_action_idle", "ground_action_start", "ground_action_move", "ground_action_turn_on_spot", "ground_action_turn_on_spot_loop", "ground_action_plant_and_turn"];
	private static readonly string[] AirActionNames = ["", "air_action_jump", "air_action_start_fall", "air_action_land"];
	private static readonly string[] DirectionNames = ["", "N", "NE", "E", "SE", "S", "SW", "W", "NW"];
}
