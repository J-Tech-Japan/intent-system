'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const packageRoot = path.resolve(__dirname, '..');
const provenanceRepositoryUrl = 'https://github.com/J-Tech-Japan/intent-system';
const platforms = [
  { directory: 'darwin-arm64', name: '@j-tech-japan/intent-cli-darwin-arm64', os: 'darwin', cpu: 'arm64', rid: 'osx-arm64' },
  { directory: 'linux-x64', name: '@j-tech-japan/intent-cli-linux-x64', os: 'linux', cpu: 'x64', rid: 'linux-x64' },
  { directory: 'win32-x64', name: '@j-tech-japan/intent-cli-win32-x64', os: 'win32', cpu: 'x64', rid: 'win-x64' },
];

function readJson(relativePath) {
  return JSON.parse(fs.readFileSync(path.join(packageRoot, relativePath), 'utf8'));
}

test('main package exposes exactly the supported optional platform packages', () => {
  const main = readJson('package.json');
  assert.equal(main.name, 'intent-system');
  assert.equal(main.bin['intent-cli'], 'bin/intent-cli.js');
  assert.deepEqual(Object.keys(main.optionalDependencies).sort(), platforms.map((item) => item.name).sort());
  assert.equal(main.scripts?.postinstall, undefined);
  for (const platform of platforms) {
    assert.equal(main.optionalDependencies[platform.name], '0.0.0-dev');
  }
});

test('platform templates declare npm selection, release RID, and checksum metadata', () => {
  for (const platform of platforms) {
    const manifest = readJson(`platforms/${platform.directory}/package.json`);
    assert.equal(manifest.name, platform.name);
    assert.deepEqual(manifest.os, [platform.os]);
    assert.deepEqual(manifest.cpu, [platform.cpu]);
    assert.equal(manifest.repository?.type, 'git');
    assert.equal(manifest.repository?.url, provenanceRepositoryUrl);
    assert.equal(manifest.intentCli.platform, platform.rid);
    assert.equal(manifest.intentCli.binarySha256, '__BINARY_SHA256__');
    assert.equal(manifest.scripts?.postinstall, undefined);
  }
});

test('package readme documents global, npx, no-postinstall, and checksum behavior', () => {
  const readme = fs.readFileSync(path.join(packageRoot, 'README.md'), 'utf8');
  assert.match(readme, /npm install -g intent-system/);
  assert.match(readme, /npx intent-system/);
  assert.match(readme, /no `postinstall`/);
  assert.match(readme, /checksum/);
});
