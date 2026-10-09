#!/usr/bin/env python3
"""Reproduce the Beutl landing-page recording in an isolated app checkout."""

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = "tests/Beutl.HeadlessUITests/Beutl.HeadlessUITests.csproj"
SCENARIO = "Beutl.HeadlessUITests.Demos.HeroDemoCaptureTests."


def run(args, *, cwd=None, env=None, capture=False):
    args = [str(arg) for arg in args]
    if not capture:
        print("+ " + " ".join(args), flush=True)
    return subprocess.run(args, cwd=cwd, env=env, check=True, text=True,
                          stdout=subprocess.PIPE if capture else None).stdout


def prepare(args):
    lock = json.loads((ROOT / "beutl.lock.json").read_text())
    revision = args.revision or lock["revision"]
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Use a full Beutl commit SHA, not a moving branch or tag.")
    checkout = ROOT / ".cache" / ("beutl-" + revision)
    if not checkout.exists():
        checkout.parent.mkdir(parents=True, exist_ok=True)
        # Publish a complete checkout only after fetching the exact revision succeeds.
        with tempfile.TemporaryDirectory(dir=checkout.parent, prefix="prepare-") as temporary:
            run(["git", "init", temporary])
            run(["git", "remote", "add", "origin", lock["repository"]], cwd=temporary)
            fetch_from = str(Path(args.source).resolve()) if args.source else "origin"
            run(["git", "fetch", "--depth=1", fetch_from, revision], cwd=temporary)
            run(["git", "checkout", "--detach", revision], cwd=temporary)
            Path(temporary).rename(checkout)
    if run(["git", "rev-parse", "HEAD"], cwd=checkout, capture=True).strip() != revision:
        raise RuntimeError(f"Unexpected revision in managed checkout: {checkout}")
    run(["git", "submodule", "update", "--init", "--recursive"], cwd=checkout)
    for patch in sorted((ROOT / "patches").glob("*.patch")):
        applied = subprocess.run(["git", "apply", "--reverse", "--check", str(patch)],
                                 cwd=checkout, capture_output=True)
        if applied.returncode == 0:
            continue
        run(["git", "apply", "--check", patch], cwd=checkout)
        run(["git", "apply", patch], cwd=checkout)
    # This directory is generated from src; edit the source here, not the managed copy.
    target = checkout / "tests/Beutl.HeadlessUITests/Demos"
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(ROOT / "src", target)
    assets = checkout / "tests/Beutl.HeadlessUITests/Assets/Demos"
    assets.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / "assets/hero-editorial.json", assets)
    shutil.copy2(ROOT / "assets/fonts/Inter_28pt-Black.ttf", assets)
    return checkout


def run_scenario(args):
    checkout = prepare(args)
    command = args.command
    env = os.environ.copy()
    env.update(BEUTL_DEMO_CULTURE="en-US", BEUTL_REQUIRE_GPU="1",
               BEUTL_DEMO_STILLS_ONLY="1" if command == "preview" else "0",
               BEUTL_DEMO_CAPTURE_ONLY="0",
               BEUTL_DEMO_OUTPUT_DIR=str(Path(args.output).resolve()),
               BEUTL_DEMO_CAMERA_SETTINGS=str(Path(args.settings).resolve()))
    if command in {"check", "record", "preview"}:
        if sys.platform != "darwin":
            raise RuntimeError("The macOS presentation uses native AppKit cursors; run it on macOS.")
        cursor_dir = ROOT / ".cache/macos-cursors"
        run(["swift", "-module-cache-path", ROOT / ".cache/swift-modules",
             ROOT / "scripts/export-macos-cursors.swift", cursor_dir])
        env["BEUTL_DEMO_CURSOR_DIR"] = str(cursor_dir)
    if command in {"camera", "verify"}:
        if not args.capture:
            raise ValueError(f"{command} requires --capture /path/to/hero-capture-directory")
        env["BEUTL_DEMO_SOURCE_DIR"] = str(Path(args.capture).resolve())
    methods = {
        "record": "Record_hero_editorial_workflow", "preview": "Record_hero_editorial_workflow",
        "camera": "Render_camera_from_recording", "verify": "Verify_saved_hero_composition",
    }
    selection = ("FullyQualifiedName~Demos.DemoCameraMotionTests|"
                 "FullyQualifiedName~Demos.DemoCursorOverlayTests|"
                 "FullyQualifiedName~Demos.DemoGraphFramingTests|"
                 "FullyQualifiedName~Demos.DemoFrameComposerTests") if command == "check" else (
                     "FullyQualifiedName=" + SCENARIO + methods[command])
    invocation = ["dotnet", "test", PROJECT, "-c", "Release", "-f", "net10.0",
                  "--filter", selection, "--logger", "console;verbosity=normal"]
    if args.no_build:
        invocation += ["--no-build", "--no-restore"]
    run(invocation, cwd=checkout, env=env)


def probe(path):
    return json.loads(run(["ffprobe", "-v", "error", "-show_entries",
                           "stream=codec_name,width,height,r_frame_rate,nb_frames:format=duration,size",
                           "-of", "json", path], capture=True))


def web_assets(args):
    source = Path(args.video).resolve()
    media = probe(source)
    stream = media["streams"][0]
    if (stream["codec_name"], stream["width"], stream["height"], stream["r_frame_rate"]) != (
            "h264", 1920, 1080, "30/1"):
        raise ValueError("Expected the approved H.264 1920x1080, 30 fps camera recording.")
    if not 60 <= float(media["format"]["duration"]) <= 120:
        raise ValueError("Expected a one- to two-minute LP recording.")
    destination = Path(args.web).resolve() / "apps/web/public/img"
    if not destination.is_dir():
        raise ValueError("--web must name a beutl-web checkout.")
    ffmpeg = os.environ.get("BEUTL_DEMO_FFMPEG", "ffmpeg")
    with tempfile.TemporaryDirectory(dir=destination, prefix=".showcase-") as temporary:
        work = Path(temporary)
        shutil.copy2(source, work / "showcase.mp4")
        run([ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-i", source,
             "-map", "0:v:0", "-an", "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32",
             "-deadline", "good", "-cpu-used", "4", "-row-mt", "1", "-threads", "8",
             "-pix_fmt", "yuv420p", work / "showcase.webm"])
        run([ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-i", source,
             "-frames:v", "1", "-update", "1", work / "showcase-poster.png"])
        webm = probe(work / "showcase.webm")
        if abs(float(webm["format"]["duration"]) - float(media["format"]["duration"])) > 1 / 30:
            raise RuntimeError("WebM timing changed during encoding.")
        for name in ["showcase.mp4", "showcase.webm"]:
            if (work / name).stat().st_size > 25 * 1024 * 1024:
                raise RuntimeError(f"{name} exceeds the 25 MiB static-asset limit.")
            run([ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-i", work / name,
                 "-map", "0:v:0", "-an", "-f", "null", "-"])
        for name in ["showcase.mp4", "showcase.webm", "showcase-poster.png"]:
            (work / name).replace(destination / name)
            print(f"Updated {destination / name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for command in ["prepare", "check", "preview", "record", "camera", "verify"]:
        sub = commands.add_parser(command)
        sub.add_argument("--source", help="Fetch the pinned revision from a local Beutl clone instead of GitHub")
        sub.add_argument("--revision", help="Try another full Beutl commit SHA in a separate managed checkout")
        sub.add_argument("--no-build", action="store_true", help="Use an already-built managed checkout")
        sub.add_argument("--settings", default=str(ROOT / "config/camera.json"))
        sub.add_argument("--output", default=str(ROOT / "captures"))
        sub.add_argument("--capture", help="Existing capture directory for camera/verify")
    web = commands.add_parser("web-assets")
    web.add_argument("--video", required=True)
    web.add_argument("--web", required=True)
    args = parser.parse_args()
    if args.command == "prepare":
        print(prepare(args))
    elif args.command == "web-assets":
        web_assets(args)
    else:
        run_scenario(args)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"Error: {error}", file=sys.stderr)
        sys.exit(1)
