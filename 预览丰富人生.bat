@echo off
cd /d "%~dp0"
start "" http://127.0.0.1:8773
python run.py --port 8773 --prehistory 0 --db "%~dp0data\unified-preview.sqlite"
pause
