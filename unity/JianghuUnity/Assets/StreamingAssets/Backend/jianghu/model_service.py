"""Optional workspace-local model host; only stop the process we launched."""
from pathlib import Path
import json
import subprocess
import time
import urllib.request
from urllib.parse import urlparse


MODEL_ID = 'qwen3.5:4b'
MODEL_FILE = 'Qwen3.5-4B-Q4_K_M.gguf'


def model_command(config, runtime=None):
    """Use the verified installation manifest for both manual and game startup."""
    if runtime is None:
        runtime = next((p/'.runtime' for p in Path(__file__).resolve().parents
                        if (p/'.runtime'/MODEL_FILE).is_file()), None)
    if runtime is None:
        raise RuntimeError('未找到Qwen3.5模型，请先运行 python scripts/setup_model.py')
    runtime = Path(runtime).resolve()
    try:
        manifest = json.loads((runtime/'manifest.json').read_text(encoding='utf-8'))
        if manifest['model_file'] != MODEL_FILE or manifest['model_id'] != MODEL_ID:
            raise ValueError('模型安装清单不是Qwen3.5 4B')
        server = (runtime/manifest['server']).resolve()
        server.relative_to(runtime)
        model = runtime/MODEL_FILE
        if not server.is_file() or not model.is_file():
            raise ValueError('模型或运行时文件缺失')
    except (OSError, ValueError, KeyError) as exc:
        raise RuntimeError('本机模型安装不完整，请运行 python scripts/setup_model.py') from exc
    parsed = urlparse(config['url'])
    if parsed.scheme != 'http' or parsed.hostname not in ('localhost', '127.0.0.1', '::1'):
        raise ValueError('模型只允许连接本机HTTP地址')
    command = [str(server), '-m', str(model), '--alias', MODEL_ID,
               '-ngl', '99', '-c', str(config.get('context_size', 8192)), '--parallel', '1',
               '--host', parsed.hostname, '--port', str(parsed.port or 80),
               '--jinja', '--reasoning-budget', '0',
               '--chat-template-kwargs', json.dumps(dict(enable_thinking=False))]
    if manifest.get('backend') == 'cuda':
        command += ['--device', 'CUDA0', '--flash-attn', 'on']
    return command, runtime


class ModelService:
    def __init__(self):
        self.process=None;self.closed=False;self.log=None

    def healthy(self,url):
        try:
            with urllib.request.urlopen(url.rstrip('/')+'/health',timeout=1) as response:
                return response.status==200
        except Exception:return False

    def check_model(self,url,expected):
        with urllib.request.urlopen(url.rstrip('/')+'/v1/models',timeout=2) as response:
            models={m['id'] for m in json.load(response).get('data',[])}
        if expected not in models:
            raise RuntimeError(f'本机服务仍运行其他模型（{", ".join(sorted(models))}），请重启本地模型服务以使用{expected}')

    def ensure(self,config):
        if self.closed:raise RuntimeError('模型会话已关闭')
        url=config['url'];parsed=urlparse(url)
        if parsed.scheme!='http' or parsed.hostname not in ('localhost','127.0.0.1','::1'):
            raise ValueError('模型只允许连接本机HTTP地址')
        if config['backend']!='llamacpp':return
        if self.healthy(url):
            self.check_model(url,config.get('model',MODEL_ID))
            return
        if not config.get('auto_start',True):raise RuntimeError('本地模型尚未启动')
        if self.process is None or self.process.poll() is not None:
            command,runtime=model_command(config)
            if self.log:self.log.close()
            self.log=(runtime/'jianghu-model.log').open('ab')
            self.process=subprocess.Popen(command,cwd=Path(command[0]).parent,
                stdout=self.log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        deadline=time.monotonic()+45
        while not self.closed and time.monotonic()<deadline:
            if self.healthy(url):
                self.check_model(url,config.get('model',MODEL_ID))
                return
            if self.process.poll() is not None:raise RuntimeError('模型启动失败，请查看 .runtime/jianghu-model.log')
            time.sleep(.25)
        raise RuntimeError('模型启动超时，规则与已有方针继续运行')

    def close(self):
        self.closed=True
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:self.process.wait(timeout=2)
            except subprocess.TimeoutExpired:self.process.kill()
        if self.log:self.log.close();self.log=None
