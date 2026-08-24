const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = 'C:\\Users\\Administrator\\Desktop\\name7';

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript',
  '.css': 'text/css',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.json': 'application/json',
  '.xml': 'text/xml',
};

function resolve(p) {
  const clean = decodeURIComponent((p || '/').split('?')[0].split('#')[0]);
  let rel = clean;
  if (rel === '/') rel = '/index.html';
  const full = path.normalize(path.join(ROOT, rel));
  if (full !== ROOT && !full.startsWith(ROOT + path.sep)) return null;
  return full;
}

const server = http.createServer((req, res) => {
  const filePath = resolve(req.url);
  if (!filePath) {
    res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('403 Forbidden');
    return;
  }
  fs.readFile(filePath, (err, data) => {
    if (err) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('404 Not Found: ' + req.url);
      return;
    }
    const headers = { 'Cache-Control': 'no-cache' };
    let name = filePath;
    if (name.endsWith('.gz')) {
      headers['Content-Encoding'] = 'gzip';
      name = name.slice(0, -3);
    }
    const ext = path.extname(name).toLowerCase();
    headers['Content-Type'] = MIME[ext] || 'application/octet-stream';
    res.writeHead(200, headers);
    res.end(data);
  });
});

const PORT = 8080;
server.on('error', (e) => {
  console.error('SERVER ERROR: ' + e.code + ' ' + e.message);
  process.exit(1);
});
server.listen(PORT, '127.0.0.1', () => {
  console.log('SERVING ' + ROOT);
  console.log('URL http://127.0.0.1:' + PORT + '/');
});
