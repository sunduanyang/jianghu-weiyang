"""Create a separate runtime project while retaining the source and license files.

The accompanying audit is deliberately stored outside the deliverable. This is
project organization, not a change of ownership or a claim of independent origin.
"""
from __future__ import annotations

import hashlib
import json
import re
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "unity/BloodyBastardsRecovery"
DEST = ROOT / "standalone/PhysicsCombatCopy"
PROJECT = DEST / "UnityProject"
AUDIT = ROOT / "output/standalone-runtime-copy-20261030"
TEXT_EXTENSIONS = {
    ".cs", ".shader", ".cginc", ".hlsl", ".compute", ".json", ".asset",
    ".prefab", ".unity", ".mat", ".meta", ".controller", ".overridecontroller",
    ".anim", ".physicsmaterial2d", ".mixer", ".rendertexture", ".txt", ".xml",
}
LICENSE_WORDS = re.compile(r"copyright|SPDX|licensed under|permission is hereby|redistribution|all rights reserved", re.I)
ORIGIN_WORDS = re.compile(r"\bAPK\b|\bARM64\b|Ghidra|Cpp2IL|IL2CPP|decompil|0x[0-9a-fA-F]{7,}\b", re.I)
METADATA_KEYS = re.compile(
    r"^(?:evidence|source_apk(?:_sha256)?|apk_sha256|source_sha256|"
    r"source_unity|runtime_parity_verified|native_addresses?|native_methods?|"
    r".*_va|.*_address|source_observations)$", re.I
)
stats = {"copied_files": 0, "changed_text_files": 0, "removed_metadata_fields": 0,
         "excluded_files": [], "embedded_packages": [], "retained_legal_comments": 0}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_inventory() -> dict[str, str]:
    return {p.relative_to(SOURCE).as_posix(): digest(p)
            for folder in ("Assets", "Packages", "ProjectSettings")
            for p in sorted((SOURCE / folder).rglob("*")) if p.is_file()}


def omitted(rel: Path) -> bool:
    parts = rel.parts
    name = rel.name.removesuffix(".meta")
    if len(parts) > 2 and parts[:3] == ("Assets", "Recovery", "Editor"):
        return True
    if parts[:2] == ("Assets", "Recovery") and any(x in name for x in ("Validation", "Diagnostics", "Replay")):
        return True
    return False


def strip_comments(text: str) -> str:
    # Match strings before comments so URL strings and shader directives survive.
    token = re.compile(
        r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\r\n]*|/\*[\s\S]*?\*/'
    )
    def replace(match: re.Match) -> str:
        value = match.group(0)
        if not value.startswith(("//", "/*")):
            return value
        if LICENSE_WORDS.search(value):
            stats["retained_legal_comments"] += 1
            return value
        return " " + "\n" * value.count("\n")
    return re.sub(r"\n[ \t]*\n(?:[ \t]*\n)+", "\n\n", token.sub(replace, text))


def metadata_clean(value):
    if isinstance(value, list):
        return [metadata_clean(v) for v in value]
    if not isinstance(value, dict):
        return value
    result = {}
    for key, item in value.items():
        if METADATA_KEYS.match(key) or (isinstance(item, str) and ORIGIN_WORDS.search(item)):
            stats["removed_metadata_fields"] += 1
            continue
        result[key] = metadata_clean(item)
    return result


def rename_identifier(name: str) -> str:
    if name.startswith("m_"):
        return name
    name = name.replace("Original", "Arena").replace("original", "arena").replace("ORIGINAL", "ARENA")
    name = name.replace("Recovery", "Runtime").replace("recovery", "runtime").replace("RECOVERY", "RUNTIME")
    name = name.replace("Recovered", "Runtime").replace("recovered", "runtime")
    if name.startswith("Arena") and name.endswith("Source"):
        name = name[:-6] + "Data"
    return name


def main():
    if DEST.exists():
        raise SystemExit(f"Refusing to overwrite existing copy: {DEST}")
    AUDIT.mkdir(parents=True, exist_ok=True)
    before = source_inventory()
    (AUDIT / "source-before.json").write_text(json.dumps(before, ensure_ascii=False, indent=2), encoding="utf-8")
    DEST.mkdir(parents=True)

    identifiers = {}
    for path in (SOURCE / "Assets/Recovery").rglob("*.cs"):
        for token in re.findall(r"\b[A-Za-z_][A-Za-z_0-9]*\b", strip_comments(path.read_text(encoding="utf-8-sig"))):
            if any(s in token for s in ("Original", "original", "ORIGINAL", "Recovery", "recovery", "RECOVERY", "Recovered", "recovered")):
                new = rename_identifier(token)
                if new != token:
                    identifiers[token] = new
    identifiers.update({
        "PlayerSource": "PlayerTemplateKey", "EnemySource": "EnemyTemplateKey",
        "SourceIdentity": "ResourceKey", "SourceRootName": "ResourceRoot",
        "bloodyIndex": "bloodIndex",
    })
    replacement = re.compile(r"\b(?:" + "|".join(re.escape(k) for k in sorted(identifiers, key=len, reverse=True)) + r")\b")

    def transform(text: str) -> str:
        text = replacement.sub(lambda m: identifiers[m.group(0)], text)
        text = text.replace("Assets/Recovery/", "Assets/Runtime/")
        text = text.replace("Bloody Bastards", "Physics Combat").replace("BloodyBastards", "PhysicsCombat")
        text = text.replace("bloody-local-", "arena-local-").replace("bloody-playtest-", "arena-playtest-")
        text = text.replace("BLOODY_", "ARENA_")
        text = re.sub(r"\bOriginal\b", "Arena", text)
        text = re.sub(r"\boriginal\b", "runtime", text)
        text = text.replace("recoveryControlsOverlay", "controlsOverlay")
        # Saves no longer echo the input APK's fingerprint.
        text = text.replace(',"source_apk_sha256",source["source_apk_sha256"].S', "")
        return text

    for folder in ("Assets", "ProjectSettings"):
        for src in sorted((SOURCE / folder).rglob("*")):
            if not src.is_file():
                continue
            rel = src.relative_to(SOURCE)
            if omitted(rel):
                stats["excluded_files"].append(rel.as_posix())
                continue
            new_parts = []
            for part in rel.parts:
                part = transform(part)
                if part == "Recovery" or part == "Recovery.meta":
                    part = part.replace("Recovery", "Runtime")
                new_parts.append(part)
            target = PROJECT.joinpath(*new_parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            if src.suffix.lower() in TEXT_EXTENSIONS:
                try:
                    raw = src.read_text(encoding="utf-8-sig")
                except UnicodeDecodeError:
                    shutil.copy2(src, target)
                    stats["copied_files"] += 1
                    continue
                value = raw
                if src.suffix.lower() == ".json" and rel.parts[:2] == ("Assets", "Recovery"):
                    value = json.dumps(metadata_clean(json.loads(raw)), ensure_ascii=False, separators=(",", ":"))
                if src.suffix.lower() in {".cs", ".shader", ".cginc", ".hlsl", ".compute"} and "AssetRipperPatches" not in rel.parts:
                    value = strip_comments(value)
                value = transform(value)
                if value != raw:
                    stats["changed_text_files"] += 1
                target.write_text(value, encoding="utf-8", newline="\n")
            else:
                shutil.copy2(src, target)
            stats["copied_files"] += 1

    # Embedded packages eliminate dependencies on research/output directories.
    manifest = json.loads((SOURCE / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
    packages = PROJECT / "Packages"
    packages.mkdir()
    for name, location in list(manifest["dependencies"].items()):
        if location.startswith("file:") and "/output/" in location.replace("\\", "/"):
            origin = Path(location[5:])
            if not (origin / "package.json").is_file():
                raise RuntimeError(f"Missing dependency: {origin}")
            shutil.copytree(origin, packages / name, ignore=shutil.ignore_patterns(".git", "node_modules", "Library", "Temp"))
            manifest["dependencies"].pop(name)
            stats["embedded_packages"].append(name)
    (packages / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")

    preview = PROJECT / "Assets/Runtime/ArenaPreview.cs"
    preview.write_text('''using System;
using System.Collections;
using UnityEngine;

public sealed partial class ArenaPreview : MonoBehaviour {
    public GameObject playerPrefab,enemyPrefab;
    public ArenaRig player,enemy;
    public ArenaBattleHud hud;
    public bool ExternalInputReplay {get;set;}
    bool automated;
    int impacts;
    IEnumerator Start() {
        var args=Environment.GetCommandLineArgs();
        InitializeDesktopPresentation(args);
        Application.targetFrameRate=60;
        Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
        Time.fixedDeltaTime=1f/60;
        arenaInput.Bind(hud==null?null:hud.transform);
        InitializeArenaEnvironment();
        yield return arenaTransition.CreateToArena(GameMode);
        AttachPlaytestSetupMenu();
    }
    ArenaRig Create(GameObject prefab,bool human,Vector3 position) {
        var go=Instantiate(prefab,position,prefab.transform.rotation);
        go.name=human?"Arena player":"Arena opponent";
        go.SetActive(true);
        var rig=go.AddComponent<ArenaRig>();rig.Initialize(human);
        return rig;
    }
    void Update() {
        HandleDesktopWindowKeys();
        AdvanceArenaHostClock();
        if(player==null||automated||ExternalInputReplay||IsTransitioning)return;
        if(HandlePlaytestSetupKeys())return;
        if(Input.GetKeyDown(KeyCode.Escape))SetPaused(!Paused);
        if((ArenaState==5||ArenaState==6)&&(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.R))) {
            if(ArenaState==6)RestartArenaMatch();else AdvanceArenaRound();
        }
        ReadArenaInput();
    }
    void OnGUI() {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-controlsOverlay")<0)return;
        GUI.color=Color.white;
        GUI.Label(new Rect(20,Screen.height-26,Screen.width-40,25),"A / D: move   Left mouse: main hand   Right mouse: off hand   Space: kick   F2: loadout   F11: fullscreen   Esc: pause");
    }
}
''', encoding="utf-8")
    # The last built scene already contains the complete object bindings. Build
    # that scene directly instead of running reconstruction/export editor code.
    editor = PROJECT / "Assets/Editor/StandaloneBuild.cs"
    editor.write_text('''using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class StandaloneBuild {
    public static void Build() {
        const string scene="Assets/Runtime/Scenes/ArenaHierarchy.unity";
        PlayerSettings.companyName="Local Runtime";
        PlayerSettings.productName="Physics Combat";
        PlayerSettings.applicationIdentifier="local.physicscombat.standalone";
        PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scene,true)};
        AssetDatabase.SaveAssets();
        string output=Path.GetFullPath("../Build/PhysicsCombat.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var result=BuildPipeline.BuildPlayer(new[]{scene},output,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Standalone build failed: "+result.summary.result);
        Debug.Log("STANDALONE_BUILD_OK "+output);
    }
}
''', encoding="utf-8")
    settings = PROJECT / "ProjectSettings/ProjectSettings.asset"
    content = settings.read_text(encoding="utf-8")
    content = re.sub(r"(?m)^  companyName:.*$", "  companyName: Local Runtime", content)
    content = re.sub(r"(?m)^  productName:.*$", "  productName: Physics Combat", content)
    settings.write_text(content, encoding="utf-8")
    build_settings = PROJECT / "ProjectSettings/EditorBuildSettings.asset"
    build_settings.write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
EditorBuildSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_Scenes:
  - enabled: 1
    path: Assets/Runtime/Scenes/ArenaHierarchy.unity
  m_configObjects: {}
''', encoding="utf-8")
    (DEST / "启动副本.bat").write_text('''@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0Build\\PhysicsCombat.exe" exit /b 1
if not exist "%~dp0Save" mkdir "%~dp0Save"
start "" "%~dp0Build\\PhysicsCombat.exe" -force-d3d11 -playtestFullscreen -playtestSetup -arenaGameMode 0 -arenaProgressPath "%~dp0Save\\progress.json" -logFile "%~dp0Save\\player.log"
endlocal
''', encoding="utf-8", newline="\r\n")
    (DEST / "用Unity打开.bat").write_text('''@echo off
start "" "D:\\Unity\\Editors\\6000.4.0f1\\Editor\\Unity.exe" -projectPath "%~dp0UnityProject"
''', encoding="utf-8", newline="\r\n")
    (DEST / "README.txt").write_text(
        "物理格斗独立运行副本\n\n"
        "启动：运行「启动副本.bat」。存档保存在本目录 Save/，首次启动生成。\n"
        "操作：A/D 移动，鼠标左/右键控制双手，空格踢击，F2 配装，F11 切换全屏，Esc 暂停。\n"
        "编辑：Unity 6000.4.0f1；项目位于 UnityProject/。\n"
        "构建：Unity 批处理执行 StandaloneBuild.Build，输出到 Build/。\n\n"
        "本目录整理不改变代码、图像、声音及其他资源的原有权利归属，也不授予额外发布许可。\n"
        "第三方依赖及其许可证保存在 UnityProject/Packages/；导入工具的署名与说明保留。\n"
        "运行时资源标识、绑定数据及物理数值按功能需要保留，未声称本副本为独立原创。\n",
        encoding="utf-8")
    (DEST / ".gitignore").write_text("UnityProject/Library/\nUnityProject/Temp/\nUnityProject/Logs/\nUnityProject/obj/\nUnityProject/UserSettings/\nSave/\n", encoding="utf-8")
    after = source_inventory()
    changed = [key for key in set(before) | set(after) if before.get(key) != after.get(key)]
    stats["original_source_unchanged"] = not changed
    stats["original_source_changed_paths"] = changed
    stats["destination"] = str(DEST)
    stats["source_files_checked"] = len(before)
    stats["limits"] = [
        "Runtime identifiers, resource geometry/content and behavior can still establish provenance.",
        "Legal notices and third-party attribution are retained; no ownership or release authorization is asserted.",
        "Only the copy is edited; this audit remains outside it.",
    ]
    (AUDIT / "copy-audit.json").write_text(json.dumps(stats, ensure_ascii=False, indent=2), encoding="utf-8")
    (AUDIT / "identifier-map.json").write_text(json.dumps(identifiers, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in stats.items() if k not in {"excluded_files", "limits"}}, ensure_ascii=False))


if __name__ == "__main__":
    main()
