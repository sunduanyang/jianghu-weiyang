@echo off
cd /d "%~dp0"
start "" http://127.0.0.1:8774
python run.py --port 8774 --population 500 --prehistory 0 --db "%~dp0data\territory-preview.sqlite"
pause
