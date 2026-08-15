'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const test = require('node:test');

const packageRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(packageRoot, '..', '..');

function makeFixture() {
  fs.mkdirSync(path.join(repoRoot, '.artifacts'), { recursive: true });
  const root = fs.mkdtempSync(path.join(repoRoot, '.artifacts', 'g702-npm-shim-'));
  const shimDirectory = path.join(root, 'node_modules', 'intent-system', 'bin');
  const platformPackages = {
    'darwin:arm64': ['intent-cli-darwin-arm64', 'intent-cli'],
    'linux:x64': ['intent-cli-linux-x64', 'intent-cli'],
    'win32:x64': ['intent-cli-win32-x64', 'intent-cli.exe'],
  };
  const platform = platformPackages[`${process.platform}:${process.arch}`];
  if (!platform) {
    throw new Error(`test host ${process.platform}/${process.arch} is outside the supported npm matrix`);
  }
  const binaryDirectory = path.join(root, 'node_modules', '@j-tech-japan', platform[0], 'bin');
  const emptyPath = path.join(root, 'empty-path');
  fs.mkdirSync(shimDirectory, { recursive: true });
  fs.mkdirSync(binaryDirectory, { recursive: true });
  fs.mkdirSync(emptyPath);
  fs.copyFileSync(path.join(packageRoot, 'bin', 'intent-cli.js'), path.join(shimDirectory, 'intent-cli.js'));

  const binary = path.join(binaryDirectory, platform[1]);
  fs.writeFileSync(binary, '#!/bin/sh\nprintf "intent-cli 9.9.9\\n"\n');
  if (!binary.endsWith('.exe')) fs.chmodSync(binary, 0o755);
  return { root, shim: path.join(shimDirectory, 'intent-cli.js'), emptyPath };
}

function invoke(fixture, environment) {
  return spawnSync(process.execPath, [fixture.shim, '--version'], {
    cwd: fixture.root,
    encoding: 'utf8',
    env: { ...process.env, ...environment },
  });
}

test('npx shim runs the optional platform binary and emits exactly one guidance line without PATH intent-cli', () => {
  const fixture = makeFixture();
  const result = invoke(fixture, {
    PATH: fixture.emptyPath,
    npm_config_user_agent: 'npx/10.9.2 node/v23.10.0 darwin arm64',
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), 'intent-cli 9.9.9');
  const lines = result.stderr.trim().split(/\r?\n/).filter(Boolean);
  assert.equal(lines.length, 1);
  assert.match(lines[0], /npm install -g intent-system/);
  assert.match(lines[0], /intent-cli/);
});

test('npx shim does not repeat guidance when intent-cli is already on PATH', () => {
  const fixture = makeFixture();
  const pathDirectory = path.join(fixture.root, 'path-with-intent-cli');
  fs.mkdirSync(pathDirectory);
  const pathCommand = path.join(pathDirectory, 'intent-cli');
  fs.writeFileSync(pathCommand, '#!/bin/sh\nexit 0\n');
  fs.chmodSync(pathCommand, 0o755);
  const result = invoke(fixture, {
    PATH: pathDirectory,
    npm_config_user_agent: 'npx/10.9.2 node/v23.10.0',
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stderr.trim(), '');
});

test('non-npx invocations do not emit the npx guidance line', () => {
  const fixture = makeFixture();
  const result = invoke(fixture, {
    PATH: fixture.emptyPath,
    npm_config_user_agent: 'npm/10.9.2 node/v23.10.0',
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stderr.trim(), '');
});

test('shim has no installation side effect or postinstall contract', () => {
  const source = fs.readFileSync(path.join(packageRoot, 'bin', 'intent-cli.js'), 'utf8');
  assert.doesNotMatch(source, /(?:spawn|exec)(?:Sync|File)?\([^)]*['"]npm['"]/s);
  const packageJson = JSON.parse(fs.readFileSync(path.join(packageRoot, 'package.json'), 'utf8'));
  assert.equal(packageJson.scripts?.postinstall, undefined);
});
