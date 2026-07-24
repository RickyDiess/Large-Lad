"""Repair generated Hammer prefabs and add deterministic editor visuals.

The PowerShell JSON converter treats ``__type`` as metadata and drops it.
This script uses the standard JSON parser so aggregate prefabs retain the
component type names s&box needs when Hammer opens them.
"""

from __future__ import annotations

import json
import uuid
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
MAPPING_ROOT = PROJECT_ROOT / "Assets" / "Prefabs" / "Mapping"
PREFAB_FILES = sorted(MAPPING_ROOT.glob("*.prefab")) + [
    PROJECT_ROOT / "Assets" / "Prefabs" / "large_lad_gameplay.prefab"
]
SCENE_FILES = [
    PROJECT_ROOT / "Assets" / "scenes" / "minimal.scene",
    PROJECT_ROOT / "Assets" / "scenes" / "map_template_test.scene",
]

PREVIEW_ONLY_NAME = "Large Lad Hammer Preview"


def infer_component_type(component: dict) -> str | None:
    keys = set(component)

    if {"Center", "Scale", "IsTrigger"} <= keys:
        return "Sandbox.BoxCollider"
    if {"BodyGroups", "Model", "RenderOptions", "Tint"} <= keys:
        return "Sandbox.ModelRenderer"
    if {"PlayerPrefab", "SpawnPoints", "StartServer"} <= keys:
        return "Sandbox.NetworkHelper"
    if {"Group", "SpawnRadius", "Capacity", "MinimumSeparation"} <= keys:
        return "LargeLadTeamSpawn"
    if "Color" in keys and "Group" not in keys:
        return "Sandbox.SpawnPoint"
    if {"HeadStartDuration", "SurvivalDuration", "IntermissionDuration"} <= keys:
        return "LargeLadMapDefinition"
    if {"Weapon", "Policy"} <= keys:
        return "LargeLadWeaponPickup"
    if {"Weapon", "AmmoAmount"} <= keys:
        return "LargeLadAmmoPickup"
    if {"Mode", "MaximumHealth", "LadStructuralDamage"} <= keys:
        return "LargeLadBarricade"
    if {"TriggerCollider", "GizmoPadding"} <= keys:
        return "LargeLadKillVolume"
    if {"MinimumPlayers", "RoundDuration", "PlayerRespawnDelay"} <= keys:
        return "LargeLadRoundManager"

    return None


def deterministic_guid(object_guid: str, label: str) -> str:
    return str(uuid.uuid5(uuid.NAMESPACE_URL, f"large-lad:{object_guid}:{label}"))


def renderer_component(object_guid: str, model: str, tint: str) -> dict:
    return {
        "__type": "Sandbox.ModelRenderer",
        "__guid": deterministic_guid(object_guid, "renderer"),
        "__enabled": True,
        "Flags": 0,
        "BodyGroups": 18446744073709551615,
        "CreateAttachments": False,
        "Model": model,
        "RenderOptions": {
            "GameLayer": True,
            "OverlayLayer": False,
            "BloomLayer": False,
            "AfterUILayer": False,
        },
        "RenderType": "On",
        "Tint": tint,
    }


def preview_object(
    parent_guid: str,
    name: str,
    model: str,
    tint: str,
    position: str,
    scale: str,
) -> dict:
    object_guid = deterministic_guid(parent_guid, name)

    return {
        "__guid": object_guid,
        "__version": 2,
        "Flags": 0,
        "Name": name,
        "Position": position,
        "Rotation": "0,0,0,1",
        "Scale": scale,
        "Tags": "",
        "Enabled": True,
        "NetworkMode": 2,
        "NetworkFlags": 0,
        "NetworkOrphaned": 0,
        "NetworkTransmit": True,
        "OwnerTransfer": 1,
        "Components": [renderer_component(object_guid, model, tint)],
        "Children": [],
    }


def component_of_type(game_object: dict, type_name: str) -> dict | None:
    return next(
        (
            component
            for component in game_object.get("Components", [])
            if component.get("__type") == type_name
        ),
        None,
    )


def map_definition_component(object_guid: str) -> dict:
    return {
        "__type": "LargeLadMapDefinition",
        "__guid": deterministic_guid(object_guid, "map-definition"),
        "__enabled": True,
        "Flags": 0,
        "HeadStartDuration": 10,
        "SurvivalDuration": 60,
        "IntermissionDuration": 5,
        "NetworkHelper": None,
        "RoundManager": None,
        "OnComponentDestroy": None,
        "OnComponentDisabled": None,
        "OnComponentEnabled": None,
        "OnComponentFixedUpdate": None,
        "OnComponentStart": None,
        "OnComponentUpdate": None,
    }


def barricade_state_object(parent_guid: str) -> dict:
    object_guid = deterministic_guid(parent_guid, "barricade-network-state")
    return {
        "__guid": object_guid,
        "__version": 2,
        "Flags": 0,
        "Name": "Barricade Network State",
        "Position": "0,0,0",
        "Rotation": "0,0,0,1",
        "Scale": "1,1,1",
        "Tags": "",
        "Enabled": True,
        "NetworkMode": 1,
        "NetworkFlags": 0,
        "NetworkOrphaned": 0,
        "NetworkTransmit": True,
        "OwnerTransfer": 1,
        "Components": [
            {
                "__type": "LargeLadBarricadeState",
                "__guid": deterministic_guid(object_guid, "component"),
                "__enabled": True,
                "Flags": 0,
                "OnComponentDestroy": None,
                "OnComponentDisabled": None,
                "OnComponentEnabled": None,
                "OnComponentFixedUpdate": None,
                "OnComponentStart": None,
                "OnComponentUpdate": None,
            }
        ],
        "Children": [],
    }


def parse_vector(text: str) -> tuple[float, float, float]:
    values = [float(value) for value in text.split(",")]
    return values[0], values[1], values[2]


def format_vector(value: tuple[float, float, float]) -> str:
    return ",".join(f"{component:.6f}".rstrip("0").rstrip(".") for component in value)


def team_spawn_object(container_guid: str, group: str, sources: list[dict]) -> dict:
    object_guid = deterministic_guid(container_guid, f"team-spawn-{group}")
    positions = [parse_vector(item.get("Position", "0,0,0")) for item in sources]
    center = tuple(
        sum(position[axis] for position in positions) / len(positions)
        for axis in range(3)
    )
    color = {
        "Lobby": "1,1,1,1",
        "SkinnyKid": "0.25,0.85,1,1",
        "Hunter": "1,0.22,0.08,1",
    }[group]
    name = {
        "Lobby": "Lobby Team Spawn",
        "SkinnyKid": "Skinny Kid Team Spawn",
        "Hunter": "Hunter Team Spawn",
    }[group]
    result = {
        "__guid": object_guid,
        "__version": 2,
        "Flags": 0,
        "Name": name,
        "Position": format_vector(center),
        "Rotation": sources[0].get("Rotation", "0,0,0,1"),
        "Scale": "1,1,1",
        "Tags": "",
        "Enabled": True,
        "NetworkMode": 2,
        "NetworkFlags": 0,
        "NetworkOrphaned": 0,
        "NetworkTransmit": True,
        "OwnerTransfer": 1,
        "Components": [
            {
                "__type": "Sandbox.SpawnPoint",
                "__guid": deterministic_guid(object_guid, "spawn-point"),
                "__enabled": True,
                "Flags": 0,
                "Color": color,
                "OnComponentDestroy": None,
                "OnComponentDisabled": None,
                "OnComponentEnabled": None,
                "OnComponentFixedUpdate": None,
                "OnComponentStart": None,
                "OnComponentUpdate": None,
            },
            {
                "__type": "LargeLadTeamSpawn",
                "__guid": deterministic_guid(object_guid, "team-spawn"),
                "__enabled": True,
                "Flags": 0,
                "Group": group,
                "SpawnRadius": 160,
                "Capacity": 16,
                "MinimumSeparation": 48,
                "OnComponentDestroy": None,
                "OnComponentDisabled": None,
                "OnComponentEnabled": None,
                "OnComponentFixedUpdate": None,
                "OnComponentStart": None,
                "OnComponentUpdate": None,
            },
        ],
        "Children": [],
    }
    add_editor_visual(result)
    return result


def upsert_editor_preview(game_object: dict, preview: dict) -> None:
    children = game_object.setdefault("Children", [])
    existing = next(
        (child for child in children if child.get("Name") == preview["Name"]),
        None,
    )

    if existing is None:
        children.append(preview)
        return

    # Hammer can persist an old child transform when a prefab instance is
    # moved or rotated. Normalize the dev visual every rebuild so it remains
    # centered on the owning collider/component instead of drifting away.
    for property_name in (
        "Position",
        "Rotation",
        "Scale",
        "Tags",
        "Enabled",
        "NetworkMode",
        "NetworkFlags",
        "NetworkOrphaned",
        "NetworkTransmit",
        "OwnerTransfer",
    ):
        existing[property_name] = preview[property_name]

    desired_renderer = preview["Components"][0]
    renderer = component_of_type(existing, "Sandbox.ModelRenderer")

    if renderer is None:
        existing.setdefault("Components", []).append(desired_renderer)
    else:
        for property_name in (
            "Model",
            "RenderOptions",
            "RenderType",
            "Tint",
        ):
            renderer[property_name] = desired_renderer[property_name]


def add_editor_visual(game_object: dict) -> None:
    object_guid = game_object["__guid"]
    weapon_pickup = component_of_type(game_object, "LargeLadWeaponPickup")
    ammo_pickup = component_of_type(game_object, "LargeLadAmmoPickup")
    barricade = component_of_type(game_object, "LargeLadBarricade")
    spawn = component_of_type(game_object, "LargeLadTeamSpawn")

    if weapon_pickup is not None:
        tint = (
            "0.25,0.85,1,1"
            if weapon_pickup.get("Weapon") == "Pistol"
            else "1,0.78,0.18,1"
        )
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                "Large Lad Pickup Dev Visual",
                "models/dev/box.vmdl",
                tint,
                "0,0,0",
                "0.5625,0.21875,0.125",
            ),
        )

    elif ammo_pickup is not None:
        tint = (
            "0.2,0.68,0.8,1"
            if ammo_pickup.get("Weapon") == "Pistol"
            else "0.8,0.624,0.144,1"
        )
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                "Large Lad Ammo Dev Visual",
                "models/dev/box.vmdl",
                tint,
                "0,0,0",
                "0.28125,0.21875,0.15625",
            ),
        )

    elif barricade is not None:
        tint = (
            "0.25,0.85,1,1"
            if barricade.get("Mode") == "SkinnyProgression"
            else "1,0.22,0.08,1"
        )
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                "Large Lad Barricade Dev Visual",
                "models/dev/box.vmdl",
                tint,
                "0,0,36",
                "3,0.5,2.25",
            ),
        )

    elif spawn is not None:
        tint = {
            "Lobby": "1,1,1,1",
            "SkinnyKid": "0.25,0.85,1,1",
            "Hunter": "1,0.22,0.08,1",
        }.get(spawn.get("Group"), "0.5,0.5,0.5,1")
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                PREVIEW_ONLY_NAME,
                "models/dev/playerstart_tint.vmdl",
                tint,
                "0,0,0",
                "1,1,1",
            ),
        )

    elif component_of_type(game_object, "LargeLadKillVolume") is not None:
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                PREVIEW_ONLY_NAME,
                "models/dev/box.vmdl",
                "1,0,0.4,0.3",
                "0,0,0",
                "4,4,1",
            ),
        )

    elif component_of_type(game_object, "LargeLadMapDefinition") is not None:
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                PREVIEW_ONLY_NAME,
                "models/dev/sphere.vmdl",
                "0.35,1,0.45,1",
                "0,0,24",
                "0.35,0.35,0.35",
            ),
        )

    elif component_of_type(game_object, "LargeLadRoundManager") is not None:
        upsert_editor_preview(
            game_object,
            preview_object(
                object_guid,
                PREVIEW_ONLY_NAME,
                "models/dev/sphere.vmdl",
                "0.72,0.35,1,1",
                "0,0,24",
                "0.35,0.35,0.35",
            ),
        )


def repair_tree(game_object: dict, source_file: Path) -> None:
    repaired_components = []

    for component in game_object.get("Components", []):
        if "__type" not in component:
            inferred = infer_component_type(component)

            if inferred is None:
                raise RuntimeError(
                    f"Cannot infer component type in {source_file}: "
                    f"{game_object.get('Name', '<unnamed>')}"
                )

            component = {"__type": inferred, **component}

        if component.get("__type") == "LargeLadSpawnMarker":
            component["__type"] = "LargeLadTeamSpawn"
            component.pop("Order", None)
            component.setdefault("SpawnRadius", 160)
            component.setdefault("Capacity", 16)
            component.setdefault("MinimumSeparation", 48)

        repaired_components.append(component)

    game_object["Components"] = repaired_components

    if component_of_type(game_object, "LargeLadBarricade") is not None:
        # The tied/generated model must remain local map content. Only this
        # lightweight child is sent as an independent network object.
        game_object["NetworkMode"] = 0
        children = game_object.setdefault("Children", [])
        if not any(
            component_of_type(child, "LargeLadBarricadeState") is not None
            for child in children
        ):
            children.append(barricade_state_object(game_object["__guid"]))

    add_editor_visual(game_object)

    for child in game_object.get("Children", []):
        repair_tree(child, source_file)


def rebuild_prefab(path: Path) -> None:
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)

    # The gameplay bootstrap is the one required rules object in every map.
    # Keep the per-map definition on the same GameObject as NetworkHelper and
    # the round manager so it cannot be omitted or streamed as a later root.
    if path.name == "large_lad_gameplay.prefab":
        root = data["RootObject"]
        if component_of_type(root, "LargeLadMapDefinition") is None:
            root.setdefault("Components", []).append(
                map_definition_component(root["__guid"])
            )

    # The Hammer template places the spawn bank as its own top-level object,
    # and the bootstrap now owns the map definition. An early aggregate build
    # nested both, which encouraged duplicate map contracts.
    if path.name == "lobby_examples_complete.prefab":
        data["RootObject"]["Children"] = [
            child
            for child in data["RootObject"].get("Children", [])
            if child.get("Name") not in {
                "Large Lad 16 Player Spawn Bank",
                "Large Lad Map Definition",
            }
        ]

    # Kept as an asset for compatibility with old maps, but hidden from the
    # Hammer creation menu to prevent a second definition beside the bootstrap.
    if path.name == "map_definition.prefab":
        data["ShowInMenu"] = False

    spawn_groups = {
        "spawn_lobby.prefab": ("Lobby Team Spawn", "Large Lad/Spawns/Lobby Team Spawn"),
        "spawn_skinny_kid.prefab": (
            "Skinny Kid Team Spawn",
            "Large Lad/Spawns/Skinny Kid Team Spawn",
        ),
        "spawn_hunter.prefab": ("Hunter Team Spawn", "Large Lad/Spawns/Hunter Team Spawn"),
    }
    if path.name in spawn_groups:
        root_name, menu_path = spawn_groups[path.name]
        data["RootObject"]["Name"] = root_name
        data["MenuPath"] = menu_path

    repair_tree(data["RootObject"], path)

    # Hammer menu entries are authoring templates, not runtime prefab links.
    # Breaking the link when placed embeds the GameObjects and their component
    # data into the VMAP/VPK.  A Hammer-launched map cannot otherwise rely on
    # the project's loose .prefab_c files being mounted alongside the map.
    data["DontBreakAsTemplate"] = False

    with path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(data, handle, indent=2)
        handle.write("\n")


def migrate_scene_object_list(objects: list[dict], container_guid: str) -> int:
    legacy = [
        item
        for item in objects
        if component_of_type(item, "LargeLadSpawnMarker") is not None
    ]
    migrated = len(legacy)

    if legacy:
        legacy_ids = {item["__guid"] for item in legacy}
        replacements = []
        for group in ("Lobby", "SkinnyKid", "Hunter"):
            sources = [
                item
                for item in legacy
                if component_of_type(item, "LargeLadSpawnMarker").get("Group") == group
            ]
            if sources:
                replacements.append(team_spawn_object(container_guid, group, sources))

        objects[:] = [item for item in objects if item["__guid"] not in legacy_ids]
        objects.extend(replacements)

    for game_object in objects:
        migrated += migrate_scene_object_list(
            game_object.setdefault("Children", []),
            game_object["__guid"],
        )

    return migrated


def rebuild_scene(path: Path) -> int:
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)

    migrated = migrate_scene_object_list(
        data.setdefault("GameObjects", []),
        deterministic_guid(path.stem, "scene-root"),
    )

    for root in data["GameObjects"]:
        repair_tree(root, path)

    for root in data["GameObjects"]:
        for game_object in walk_game_objects(root):
            helper = component_of_type(game_object, "Sandbox.NetworkHelper")
            if helper is not None:
                helper["SpawnPoints"] = []

    clear_network_helper_spawn_overrides(data)

    with path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(data, handle, indent=2)
        handle.write("\n")

    return migrated


def clear_network_helper_spawn_overrides(value) -> None:
    """Remove prefab-instance links to the retired ordered lobby markers."""
    if isinstance(value, dict):
        patch = value.get("__PrefabInstancePatch")
        if isinstance(patch, dict):
            overrides = patch.get("PropertyOverrides", [])
            patch["PropertyOverrides"] = [
                override
                for override in overrides
                if override.get("Property") != "SpawnPoints"
            ]

        for child in value.values():
            clear_network_helper_spawn_overrides(child)
    elif isinstance(value, list):
        for child in value:
            clear_network_helper_spawn_overrides(child)


def walk_game_objects(game_object: dict):
    yield game_object
    for child in game_object.get("Children", []):
        yield from walk_game_objects(child)


def main() -> None:
    for prefab_file in PREFAB_FILES:
        rebuild_prefab(prefab_file)
        print(f"Rebuilt {prefab_file.relative_to(PROJECT_ROOT)}")

    for scene_file in SCENE_FILES:
        migrated = rebuild_scene(scene_file)
        print(
            f"Rebuilt {scene_file.relative_to(PROJECT_ROOT)} "
            f"({migrated} legacy spawn markers migrated)"
        )


if __name__ == "__main__":
    main()
