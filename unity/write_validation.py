"""Write the migration evidence index only after the final player and restart pass."""
from datetime import datetime, timezone, timedelta
import hashlib
import json
from pathlib import Path
import re
import sqlite3

root = Path(__file__).resolve().parent
acceptance = json.loads((root / 'validation/accepted/acceptance.json').read_text(encoding='utf-8'))
restart = json.loads((root / 'validation/accepted-restart/restart.json').read_text(encoding='utf-8'))
assert acceptance['passed'] and restart['passed'], 'Acceptance is not complete'
regression = (root / 'backend-regression.log').read_text(encoding='utf-8')
assert re.search(r'Ran 168 tests', regression) and regression.rstrip().endswith('OK')
build_log = (root / 'build.log').read_text(encoding='utf-8')
assert 'JIANGHU_BUILD_OK' in build_log and 'error CS' not in build_log
manifest = json.loads((root / 'migration-manifest.json').read_text(encoding='utf-8'))
backend = root / 'JianghuUnity/Assets/StreamingAssets/Backend'
assert all(hashlib.sha256((backend / 'jianghu' / name).read_bytes()).hexdigest() == digest ==
           hashlib.sha256((root.parent / 'jianghu' / name).read_bytes()).hexdigest()
           for name, digest in manifest['backend_files'].items())
with sqlite3.connect((backend / 'seed-world.sqlite').resolve().as_uri() + '?mode=ro', uri=True) as db:
    assert db.execute('pragma integrity_check').fetchone()[0] == 'ok'
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
index = {'verified_at': datetime.now(timezone(timedelta(hours=8))).isoformat(),
         'unity': acceptance['unity'], 'windows_build': 'Build/江湖未央.exe',
         'executable_sha256': sha(root / 'Build/江湖未央.exe'),
         'assembly_sha256': sha(root / 'Build/江湖未央_Data/Managed/Assembly-CSharp.dll'),
         'snapshot_sha256': sha(backend / 'seed-world.sqlite'),
         'engine_source_files_matched': len(manifest['backend_files']),
         'backend_regression_tests': 168, 'native_player_checks': len(acceptance['checks']),
         'restart_checks': len(restart['checks']), 'real_model_calls_added': 0,
         'graphics': 'Direct3D11',
         'native_acceptance': 'validation/accepted/acceptance.json',
         'restart_acceptance': 'validation/accepted-restart/restart.json'}
(root / 'validation-summary.json').write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding='utf-8')
rig = next(c['facts'] for c in acceptance['checks'] if c['check'].startswith('126 native rig'))
report = f'''# Unity 迁移验收

验证日期：2026-10-26（Asia/Taipei）。Unity {acceptance['unity']}，Windows x64，Mono 发布构建，Direct3D11。

- 原规则测试：168 / 168 通过，详见 `backend-regression.log`。
- 最终 Unity 程序：{len(acceptance['checks'])} / {len(acceptance['checks'])} 项检查通过，详见 `validation/accepted/acceptance.json`；使用独立 `validation/accepted-save/`，不是正式存档。程序与重启验收均经正式「启动Unity版.bat」入口启动。
- 关闭并重新启动：{len(restart['checks'])} / {len(restart['checks'])} 项通过；月份、主角完整行旅状态与行囊一致，详见 `validation/accepted-restart/restart.json`。
- 24个规则源码文件与本次来源哈希一致；迁移快照通过 SQLite 完整性检查。来源、原档路径和源码哈希见 `migration-manifest.json`。
- 126组原版皮影姿势与 Unity 原生关节坐标对比通过，最大锚点误差 {rig['max_anchor_error']:.8f} 原始坐标单位。分件仍使用原 SVG 轮廓，纹理是可重建的运行资源。
- 随游戏附带的私有 Python 运行时实测可导入 sqlite3、ctypes、ssl 和游戏引擎；最终验收由该运行时执行。
- 新增真实模型调用：0。模型功能沿用原接口，但本次没有做真实推理质量或性能验收。

检查覆盖了全部9个原生界面的渲染、原世界载入、人物查看、赠银、交谈、拜访、物品购入/卖出、装备/卸下、研读、制药、赠物、满血用药拒绝、切磋招架与认输、城内移动、跨城行旅、月结、重复请求拒绝、练武、保存、旧档副本导入和新世界创建。操作检查通过 Unity 程序内的正式客户端调用本机引擎，并保存请求和结果；没有把这些检查表述为逐按钮人工游玩验收。

视觉截图在 `validation/accepted/`，已查看舆图、城中图、行囊、势力、武学、战斗和皮影混搭页面。图形验收使用正常窗口；早期隐藏窗口截图失败的诊断记录保留在其他 validation 子目录，不作为最终验收。本机同时打开 Unity 编辑器时曾出现 DX12 初始化原生崩溃，因此工程构建与两个启动入口均指定 DX11；原崩溃日志保留在 `validation/launcher-check.log`。

这次迁移采用 Unity 原生客户端与原规则引擎组成的本机架构，并非所有 Python 已改写成 C#。布局、筛选控件和地图绘制方式有调整。皮影仍是独立试样，没有新增 NPC 外观装备联动。没有宣称数值平衡、长期模拟、全人口 AI 或发布商店验收。

最终构建与源码/快照哈希索引：`validation-summary.json`。后续任何源码或资源修改均需重新构建和核验。
'''
(root / 'VALIDATION.md').write_text(report, encoding='utf-8')
print(json.dumps(index, ensure_ascii=False, indent=2))
