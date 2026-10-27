"""Optional workspace-local model host; only stop the process we launched."""
from pathlib import Path
import os
import subprocess
import time
import urllib.request
from urllib.parse import urlparse


class ModelService:
    def __init__(self):
        self.process=None;self.closed=False;self.log=None

    def healthy(self,url):
        try:
            with urllib.request.urlopen(url.rstrip('/')+'/health',timeout=1) as response:
                return response.status==200
        except Exception:return False

    def ensure(self,config):
        if self.closed:raise RuntimeError('模型会话已关闭')
        url=config['url'];parsed=urlparse(url)
        if parsed.scheme!='http' or parsed.hostname not in ('localhost','127.0.0.1','::1'):
            raise ValueError('模型只允许连接本机HTTP地址')
        if config['backend']!='llamacpp':return
        if self.healthy(url):return
        if not config.get('auto_start',True):raise RuntimeError('本地模型尚未启动')
        if self.process is None or self.process.poll() is not None:
            roots=[p/'.runtime' for p in Path(__file__).resolve().parents]
            runtime=next((p for p in roots if (p/'Qwen3-4B-Q4_K_M.gguf').exists() and (p/'llama/llama-server.exe').exists()),None)
            if runtime is None:raise RuntimeError('未找到本机模型，请先运行启动本地模型或完成模型安装')
            self.log=(runtime/'jianghu-model.log').open('ab')
            self.process=subprocess.Popen([str(runtime/'llama/llama-server.exe'),'-m',str(runtime/'Qwen3-4B-Q4_K_M.gguf'),
                '-ngl','99','-c',str(config.get('context_size',8192)),'--parallel','1','--host',parsed.hostname,
                '--port',str(parsed.port or 80),'--jinja','--reasoning-budget','0'],cwd=runtime/'llama',
                stdout=self.log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        deadline=time.monotonic()+45
        while not self.closed and time.monotonic()<deadline:
            if self.healthy(url):return
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
