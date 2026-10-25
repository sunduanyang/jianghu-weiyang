@echo off
cd /d "%~dp0"
start "" http://127.0.0.1:8775
python run.py --port 8775 --population 500 --prehistory 0 --db "%~dp0data\city-preview.sqlite"
pause
