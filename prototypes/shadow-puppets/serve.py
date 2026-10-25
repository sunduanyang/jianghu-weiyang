"""Local art prototype only; no game state or model service is opened."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--port', type=int, default=8789)
args = parser.parse_args()
root = Path(__file__).resolve().parent
SimpleHTTPRequestHandler.extensions_map.update({'.svg': 'image/svg+xml', '.js': 'application/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8'})
server = ThreadingHTTPServer(('127.0.0.1', args.port), partial(SimpleHTTPRequestHandler, directory=str(root)))
print(f'Shadow-puppet preview: http://127.0.0.1:{args.port}/', flush=True)
try:
    server.serve_forever()
except KeyboardInterrupt:
    pass
finally:
    server.server_close()
