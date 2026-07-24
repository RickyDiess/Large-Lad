"""Expand Scene prefab links embedded in an s&box VMAP.

Hammer-launched maps must carry their gameplay GameObjects in the compiled
map.  This tool resolves selected Scene prefab instances into ordinary scene
objects while retaining instance GUIDs, cross-object references, and authored
property overrides.  It creates a sibling backup before replacing the VMAP.
"""

from __future__ import annotations

import argparse
import copy
import json
import os
import re
import shutil
import subprocess
import tempfile
import uuid
from datetime import datetime
from pathlib import Path

from rebuild_mapping_prefabs import add_editor_visual, barricade_state_object


PROJECT_ROOT = Path(__file__).resolve().parents[1]
ASSET_ROOT = PROJECT_ROOT / "Assets"
DMXCONVERT = Path(
    r"F:\SteamLibrary\steamapps\common\sbox\bin\win64\dmxconvert.exe"
)

EMBED_PREFABS = {
    "prefabs/large_lad_gameplay.prefab",
    "prefabs/mapping/spawn_lobby.prefab",
    "prefabs/mapping/spawn_skinny_kid.prefab",
    "prefabs/mapping/spawn_hunter.prefab",
    "prefabs/mapping/barricade_lad_shortcut.prefab",
}

SERIALIZED_SCENE_PATTERN = re.compile(
    r'("SerializedScene"\s+"string"\s+)("(?:\\.|[^"\\])*")'
)


def run_dmxconvert(*arguments: str) -> None:
    result = subprocess.run(
        [str(DMXCONVERT), *arguments],
        check=False,
        capture_output=True,
        text=True,
    )

    if result.returncode != 0:
        raise RuntimeError(
            "dmxconvert failed:\n"
            f"{result.stdout}\n{result.stderr}"
        )


def load_serialized_scene(kv2_path: Path) -> tuple[str, re.Match[str], dict]:
    document = kv2_path.read_text(encoding="utf-8")
    match = SERIALIZED_SCENE_PATTERN.search(document)

    if match is None:
        raise RuntimeError("VMAP does not contain a SerializedScene field.")

    scene_text = json.loads(match.group(2))
    return document, match, json.loads(scene_text)


def replace_guid_references(value, id_map: dict[str, str]):
    if isinstance(value, dict):
        return {
            key: replace_guid_references(child, id_map)
            for key, child in value.items()
        }

    if isinstance(value, list):
        return [replace_guid_references(child, id_map) for child in value]

    if isinstance(value, str):
        return id_map.get(value, value)

    return value


def find_serialized_object(value, object_guid: str) -> dict | None:
    if isinstance(value, dict):
        if value.get("__guid") == object_guid:
            return value

        for child in value.values():
            found = find_serialized_object(child, object_guid)
            if found is not None:
                return found

    elif isinstance(value, list):
        for child in value:
            found = find_serialized_object(child, object_guid)
            if found is not None:
                return found

    return None


def instantiate_prefab(instance: dict) -> dict:
    prefab_path = instance["__Prefab"].replace("/", os.sep)
    source_path = ASSET_ROOT / prefab_path

    with source_path.open("r", encoding="utf-8") as handle:
        root = copy.deepcopy(json.load(handle)["RootObject"])

    id_map = instance.get("__PrefabIdToInstanceId", {})
    root = replace_guid_references(root, id_map)

    patch = instance.get("__PrefabInstancePatch", {})
    if patch.get("AddedObjects") or patch.get("RemovedObjects") or patch.get("MovedObjects"):
        raise RuntimeError(
            f"{instance['__Prefab']} has structural instance overrides; "
            "automatic expansion was stopped."
        )

    for override in patch.get("PropertyOverrides", []):
        source_guid = override["Target"]["IdValue"]
        target_guid = id_map.get(source_guid, source_guid)
        target = find_serialized_object(root, target_guid)

        if target is None:
            raise RuntimeError(
                f"Could not apply {override['Property']} override to {target_guid}."
            )

        target[override["Property"]] = replace_guid_references(
            copy.deepcopy(override["Value"]), id_map
        )

    return root


def configure_hammer_brushes(game_object: dict) -> None:
    components = game_object.get("Components", [])
    hammer_mesh = next(
        (item for item in components if item.get("__type") == "Sandbox.HammerMesh"),
        None,
    )
    kill_volume = next(
        (item for item in components if item.get("__type") == "LargeLadKillVolume"),
        None,
    )
    barricade = next(
        (item for item in components if item.get("__type") == "LargeLadBarricade"),
        None,
    )

    if hammer_mesh is not None and kill_volume is not None:
        hammer_mesh.update(
            Static=True,
            UseCollision=True,
            UseRenderer=False,
            IsTrigger=True,
        )
        if game_object.get("Name") == "GameObject":
            game_object["Name"] = "Kill Volume Brush"

    if hammer_mesh is not None and barricade is not None:
        hammer_mesh.update(
            Static=True,
            UseCollision=True,
            UseRenderer=True,
            IsTrigger=False,
        )
        game_object["NetworkMode"] = 0
        children = game_object.setdefault("Children", [])
        if not any(
            component_of_type(child, "LargeLadBarricadeState") is not None
            for child in children
        ):
            children.append(barricade_state_object(game_object["__guid"]))
        if game_object.get("Name") == "GameObject":
            game_object["Name"] = "Skinny Progression Barricade Brush"

    for child in game_object.get("Children", []):
        configure_hammer_brushes(child)


def normalize_editor_visuals(game_object: dict) -> None:
    """Keep prefab-style previews centered without decorating tied brushes."""
    components = game_object.get("Components", [])
    is_tied_hammer_brush = any(
        item.get("__type") == "Sandbox.HammerMesh"
        for item in components
    )

    if not is_tied_hammer_brush:
        add_editor_visual(game_object)

    for child in game_object.get("Children", []):
        normalize_editor_visuals(child)


def walk_game_objects(game_object: dict):
    yield game_object
    for child in game_object.get("Children", []):
        yield from walk_game_objects(child)


def component_of_type(game_object: dict, type_name: str) -> dict | None:
    return next(
        (
            component
            for component in game_object.get("Components", [])
            if component.get("__type") == type_name
        ),
        None,
    )


def parse_vector(text: str) -> tuple[float, float, float]:
    values = [float(value) for value in text.split(",")]
    if len(values) != 3:
        raise RuntimeError(f"Invalid serialized Vector3: {text}")
    return values[0], values[1], values[2]


def format_vector(value: tuple[float, float, float]) -> str:
    return ",".join(f"{component:.6f}".rstrip("0").rstrip(".") for component in value)


def make_team_spawn(parent_guid: str, group: str, source_objects: list[dict]) -> dict:
    count = len(source_objects)
    positions = [parse_vector(item.get("Position", "0,0,0")) for item in source_objects]
    center = tuple(sum(value[index] for value in positions) / count for index in range(3))
    object_guid = str(
        uuid.uuid5(uuid.NAMESPACE_URL, f"large-lad:{parent_guid}:team-spawn:{group}")
    )
    color = {
        "Lobby": "1,1,1,1",
        "SkinnyKid": "0.25,0.85,1,1",
        "Hunter": "1,0.22,0.08,1",
    }[group]
    display_name = {
        "Lobby": "Lobby Team Spawn",
        "SkinnyKid": "Skinny Kid Team Spawn",
        "Hunter": "Hunter Team Spawn",
    }[group]
    game_object = {
        "__guid": object_guid,
        "__version": 2,
        "Flags": 0,
        "Name": display_name,
        "Position": format_vector(center),
        "Rotation": source_objects[0].get("Rotation", "0,0,0,1"),
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
                "__guid": str(uuid.uuid5(uuid.NAMESPACE_URL, f"{object_guid}:spawn-point")),
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
                "__guid": str(uuid.uuid5(uuid.NAMESPACE_URL, f"{object_guid}:team-spawn")),
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
    add_editor_visual(game_object)
    return game_object


def migrate_legacy_spawns(scene: dict) -> int:
    migrated_count = 0

    for root in scene.get("GameObjects", []):
        legacy_children = [
            item
            for item in root.get("Children", [])
            if component_of_type(item, "LargeLadSpawnMarker") is not None
        ]

        if legacy_children:
            replacements = []
            for group in ("Lobby", "SkinnyKid", "Hunter"):
                group_objects = [
                    item
                    for item in legacy_children
                    if component_of_type(item, "LargeLadSpawnMarker").get("Group") == group
                ]
                if group_objects:
                    replacements.append(make_team_spawn(root["__guid"], group, group_objects))

            legacy_ids = {item["__guid"] for item in legacy_children}
            root["Children"] = [
                item for item in root.get("Children", []) if item["__guid"] not in legacy_ids
            ] + replacements
            root["Name"] = "Large Lad Team Spawns"
            migrated_count += len(legacy_children)

    # Clean stale NetworkHelper references to the removed one-player markers.
    for game_object in (
        item
        for root in scene.get("GameObjects", [])
        for item in walk_game_objects(root)
    ):
        helper = component_of_type(game_object, "Sandbox.NetworkHelper")
        if helper is not None:
            helper["SpawnPoints"] = []

        marker = component_of_type(game_object, "LargeLadSpawnMarker")
        if marker is not None:
            marker["__type"] = "LargeLadTeamSpawn"
            marker.pop("Order", None)
            marker.setdefault("SpawnRadius", 160)
            marker.setdefault("Capacity", 16)
            marker.setdefault("MinimumSeparation", 48)
            migrated_count += 1

    return migrated_count


def ensure_bootstrap_map_definition(scene: dict) -> bool:
    objects = [
        item
        for root in scene.get("GameObjects", [])
        for item in walk_game_objects(root)
    ]
    bootstrap = next(
        (
            item
            for item in objects
            if component_of_type(item, "Sandbox.NetworkHelper") is not None
            and component_of_type(item, "LargeLadRoundManager") is not None
        ),
        None,
    )

    if bootstrap is None:
        return False

    definitions = [
        component_of_type(item, "LargeLadMapDefinition")
        for item in objects
    ]
    definitions = [definition for definition in definitions if definition is not None]

    if component_of_type(bootstrap, "LargeLadMapDefinition") is not None:
        return False

    # Preserve authored timing from an older standalone definition when one
    # exists; otherwise use the stable map-contract defaults.
    source = definitions[0] if definitions else {}
    component_guid = str(
        uuid.uuid5(
            uuid.NAMESPACE_URL,
            f"large-lad:{bootstrap['__guid']}:map-definition",
        )
    )
    bootstrap.setdefault("Components", []).append(
        {
            "__type": "LargeLadMapDefinition",
            "__guid": component_guid,
            "__enabled": True,
            "Flags": 0,
            "HeadStartDuration": source.get("HeadStartDuration", 10),
            "SurvivalDuration": source.get("SurvivalDuration", 60),
            "IntermissionDuration": source.get("IntermissionDuration", 5),
            "NetworkHelper": None,
            "RoundManager": None,
            "OnComponentDestroy": None,
            "OnComponentDisabled": None,
            "OnComponentEnabled": None,
            "OnComponentFixedUpdate": None,
            "OnComponentStart": None,
            "OnComponentUpdate": None,
        }
    )
    return True


def next_backup_path(source: Path) -> Path:
    # Keep binary safety copies outside Assets so s&box does not compile them
    # as additional playable maps.
    backup_root = PROJECT_ROOT.parent / "hammer-map-backups"
    backup_root.mkdir(parents=True, exist_ok=True)
    preferred = backup_root / f"{source.stem}.prefab-links-backup{source.suffix}"
    if not preferred.exists():
        return preferred

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return backup_root / f"{source.stem}.prefab-links-backup-{timestamp}{source.suffix}"


def migrate(source: Path) -> tuple[Path, int, bool, int]:
    source = source.resolve()
    maps_root = (ASSET_ROOT / "maps").resolve()

    if maps_root not in source.parents or source.suffix.lower() != ".vmap":
        raise RuntimeError(f"Refusing to modify unexpected VMAP path: {source}")

    if not source.exists():
        raise FileNotFoundError(source)

    backup = next_backup_path(source)
    shutil.copy2(source, backup)

    with tempfile.TemporaryDirectory(prefix="large-lad-vmap-") as temp_dir:
        temp_root = Path(temp_dir)
        kv2_path = temp_root / "map-keyvalues2.vmap"
        migrated_binary = temp_root / "map-migrated.vmap"
        verification_kv2 = temp_root / "map-verification.vmap"

        run_dmxconvert(
            "-i", str(source),
            "-o", str(kv2_path),
            "-oe", "keyvalues2",
            "-of", "vmap",
        )

        document, match, scene = load_serialized_scene(kv2_path)
        embedded_count = 0
        migrated_objects = []

        for game_object in scene.get("GameObjects", []):
            prefab = game_object.get("__Prefab")
            if prefab in EMBED_PREFABS:
                migrated_objects.append(instantiate_prefab(game_object))
                embedded_count += 1
            else:
                migrated_objects.append(game_object)

        scene["GameObjects"] = migrated_objects
        added_map_definition = ensure_bootstrap_map_definition(scene)
        migrated_spawns = migrate_legacy_spawns(scene)
        for game_object in scene["GameObjects"]:
            configure_hammer_brushes(game_object)
            normalize_editor_visuals(game_object)

        scene_text = json.dumps(scene, indent=2).replace("\n", "\r\n")
        replacement = match.group(1) + json.dumps(scene_text)
        migrated_document = document[: match.start()] + replacement + document[match.end() :]
        kv2_path.write_text(migrated_document, encoding="utf-8", newline="")

        run_dmxconvert(
            "-i", str(kv2_path),
            "-ie", "keyvalues2",
            "-o", str(migrated_binary),
            "-oe", "binary",
            "-of", "vmap",
        )

        if not migrated_binary.exists() or migrated_binary.stat().st_size == 0:
            raise RuntimeError("Migrated VMAP was not produced.")

        run_dmxconvert(
            "-i", str(migrated_binary),
            "-o", str(verification_kv2),
            "-oe", "keyvalues2",
            "-of", "vmap",
        )
        _, _, verified_scene = load_serialized_scene(verification_kv2)
        unresolved = [
            item.get("__Prefab")
            for item in verified_scene.get("GameObjects", [])
            if item.get("__Prefab") in EMBED_PREFABS
        ]
        if unresolved:
            raise RuntimeError(f"Prefab links remain after migration: {unresolved}")

        # os.replace cannot cross Windows volumes. Stage the verified file
        # beside the destination, then perform the atomic same-volume swap.
        staged_binary = source.with_name(f".{source.name}.large-lad-migrating")
        shutil.copy2(migrated_binary, staged_binary)
        os.replace(staged_binary, source)

    return backup, embedded_count, added_map_definition, migrated_spawns


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("vmap", type=Path)
    arguments = parser.parse_args()
    backup, count, added_map_definition, migrated_spawns = migrate(arguments.vmap)
    print(f"Embedded {count} Scene prefab instance(s).")
    print(f"Added map definition to bootstrap: {added_map_definition}")
    print(f"Migrated legacy one-player spawns: {migrated_spawns}")
    print(f"Backup: {backup}")


if __name__ == "__main__":
    main()
