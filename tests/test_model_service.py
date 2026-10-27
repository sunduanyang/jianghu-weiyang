"""Regression checks for selecting the upgraded local model without using saves."""
import json
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from jianghu.ai import continuous_config
from jianghu.model_service import MODEL_FILE, MODEL_ID, ModelService, model_command


class ModelUpgradeTests(unittest.TestCase):
    def test_legacy_default_migrates_without_resetting_player_preferences(self):
        config=continuous_config(dict(enabled=True,model='qwen3:4b',queue_limit=3,cooldown_months=6))
        self.assertEqual(config['model'],MODEL_ID)
        self.assertEqual((config['enabled'],config['queue_limit'],config['cooldown_months']),(True,3,6))

    def test_custom_endpoints_and_ollama_models_are_preserved(self):
        for overrides in (dict(backend='ollama',model='qwen3:4b'),
                          dict(url='http://127.0.0.1:9999',model='qwen3:4b'),
                          dict(model='my-custom-model')):
            self.assertEqual(continuous_config(overrides)['model'],overrides['model'])

    def installation(self,root):
        (root/MODEL_FILE).touch()
        (root/'cuda').mkdir()
        (root/'cuda/llama-server.exe').touch()
        (root/'manifest.json').write_text(json.dumps(dict(model_file=MODEL_FILE,model_id=MODEL_ID,
                                                        server='cuda/llama-server.exe',backend='cuda')),encoding='utf-8')

    def test_manual_and_game_command_uses_installed_cuda_model(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);self.installation(root)
            command,_=model_command(dict(url='http://127.0.0.1:11435',context_size=8192),root)
            self.assertEqual(Path(command[command.index('-m')+1]).resolve(),(root/MODEL_FILE).resolve())
            self.assertEqual(command[command.index('--alias')+1],MODEL_ID)
            self.assertEqual(command[command.index('--device')+1],'CUDA0')
            self.assertEqual(json.loads(command[command.index('--chat-template-kwargs')+1]),dict(enable_thinking=False))

    def test_missing_installation_fails_instead_of_silently_using_old_model(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaisesRegex(RuntimeError,'安装不完整'):
                model_command(dict(url='http://127.0.0.1:11435'),Path(folder))

    def test_manifest_cannot_launch_a_server_outside_the_runtime(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);self.installation(root)
            manifest=json.loads((root/'manifest.json').read_text())
            manifest['server']='../llama-server.exe'
            (root/'manifest.json').write_text(json.dumps(manifest))
            with self.assertRaisesRegex(RuntimeError,'安装不完整'):
                model_command(dict(url='http://127.0.0.1:11435'),root)

    def test_existing_old_server_is_not_reported_as_the_new_model(self):
        service=ModelService()
        old=io.BytesIO(json.dumps(dict(data=[dict(id='qwen3:4b')])).encode())
        with patch.object(service,'healthy',return_value=True),patch('urllib.request.urlopen',return_value=old):
            with self.assertRaisesRegex(RuntimeError,'其他模型'):
                service.ensure(continuous_config())
        self.assertIsNone(service.process)


if __name__=='__main__':
    unittest.main()
