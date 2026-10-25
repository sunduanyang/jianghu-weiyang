@echo off
cd /d "%~dp0"
start "" http://127.0.0.1:8767
python run.py --port 8767
pause
