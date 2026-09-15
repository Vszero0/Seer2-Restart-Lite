"""Import the QQNT small yellow-face resources used by the story system.

This is an offline asset preparation tool.  It deliberately reads only the
"小黄脸表情" group from QQNT's local panel configuration; super expressions
and the standard emoji group are not imported.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

from PIL import Image


FACE_SIZE = 128
SHEET_COLUMNS = 8
DEFAULT_QQ_ROOT = Path(r"C:\Program Files\Tencent\QQNT")
GLOBAL_RESOURCE_ROOT = Path(
    r"C:\Users\Jiangnan\Documents\Tencent Files\nt_qq\global\nt_data\Emoji\emoji-resource"
)


def find_latest_version(root: Path) -> Path:
    versions = [path for path in root.glob("versions/*") if path.is_dir()]
    if not versions:
        raise FileNotFoundError(f"QQNT versions directory not found: {root}")
    return sorted(versions, key=lambda path: path.name)[-1]


def load_small_faces(config_path: Path) -> list[dict]:
    with config_path.open("r", encoding="utf-8") as stream:
        config = json.load(stream)

    for group in config["normalPanelResult"]["SysEmojiGroupList"]:
        if group.get("groupName") == "小黄脸表情":
            return list(group.get("SysEmojiList", []))

    raise RuntimeError(f'QQNT config has no "小黄脸表情" group: {config_path}')


def read_apng_frames(path: Path) -> tuple[list[Image.Image], list[int]]:
    with Image.open(path) as source:
        frames: list[Image.Image] = []
        durations: list[int] = []
        for frame_index in range(source.n_frames):
            source.seek(frame_index)
            frame = source.convert("RGBA").copy()
            if frame.size != (FACE_SIZE, FACE_SIZE):
                raise RuntimeError(f"Unexpected QQ face size in {path}: {frame.size}")
            frames.append(frame)
            duration = source.info.get("duration", 41.6666667)
            durations.append(max(1, int(round(float(duration)))))
        return frames, durations


def save_preview(source_path: Path, destination: Path) -> None:
    with Image.open(source_path) as source:
        image = source.convert("RGBA")
        image.save(destination, format="PNG", optimize=True)


def build_sheet(frames: list[Image.Image], destination: Path) -> None:
    rows = (len(frames) + SHEET_COLUMNS - 1) // SHEET_COLUMNS
    sheet = Image.new("RGBA", (SHEET_COLUMNS * FACE_SIZE, rows * FACE_SIZE), (0, 0, 0, 0))
    for frame_index, frame in enumerate(frames):
        column = frame_index % SHEET_COLUMNS
        top_row = frame_index // SHEET_COLUMNS
        sheet.paste(frame, (column * FACE_SIZE, top_row * FACE_SIZE))
    sheet.save(destination, format="PNG", optimize=True)


def main() -> None:
    repository_root = Path(__file__).resolve().parents[1]
    qq_root = Path(os.environ.get("STORY_QQ_ROOT", str(DEFAULT_QQ_ROOT)))
    version_root = find_latest_version(qq_root)
    config_path = version_root / "resources/app/resource/default-emojis/default_config.json"
    preview_root = version_root / "resources/app/resource/default-emojis"
    apng_root = GLOBAL_RESOURCE_ROOT / "sysface_res/apng"

    output_root = repository_root / "Assets/Resources/Story/Expressions/QQFace"
    preview_output = output_root / "Preview"
    sheet_output = output_root / "SpriteSheets"
    preview_output.mkdir(parents=True, exist_ok=True)
    sheet_output.mkdir(parents=True, exist_ok=True)

    entries: list[dict] = []
    generated_sheet_names: set[str] = set()
    small_faces = load_small_faces(config_path)
    for face in small_faces:
        qq_id = str(face["emojiId"])
        stable_id = f"qq_face_{qq_id}"
        preview_source = preview_root / f"{qq_id}.png"
        if not preview_source.exists():
            raise FileNotFoundError(f"QQ preview is missing: {preview_source}")

        preview_path = preview_output / f"{stable_id}.png"
        save_preview(preview_source, preview_path)

        apng_path = apng_root / f"s{qq_id}.png"
        frames: list[Image.Image] = []
        durations: list[int] = []
        if apng_path.exists():
            frames, durations = read_apng_frames(apng_path)
            if len(frames) <= 1:
                frames = []
                durations = []
            else:
                sheet_path = sheet_output / f"{stable_id}.png"
                build_sheet(frames, sheet_path)
                generated_sheet_names.add(sheet_path.name)

        duration_overrides = durations if durations and len(set(durations)) > 1 else []
        entry = {
            "id": stable_id,
            "qqId": qq_id,
            "displayName": str(face.get("describe", "")).lstrip("/"),
            "qzoneCode": str(face.get("qzoneCode", "")),
            "previewPath": f"Story/Expressions/QQFace/Preview/{stable_id}",
            "spriteSheetPath": (
                f"Story/Expressions/QQFace/SpriteSheets/{stable_id}" if frames else ""
            ),
            "frameCount": len(frames),
            "frameDurationMs": durations[0] if durations else 0,
            "frameDurationsMs": duration_overrides,
        }
        entries.append(entry)
        print(f"{stable_id}: preview + {len(frames)} frames")

    for stale_sheet in sheet_output.glob("*.png"):
        if stale_sheet.name not in generated_sheet_names:
            stale_sheet.unlink()

    catalog_path = output_root / "QQFaceCatalog.json"
    catalog_path.write_text(
        json.dumps({"version": 1, "expressions": entries}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Imported {len(entries)} small yellow faces into {output_root}")


if __name__ == "__main__":
    main()
